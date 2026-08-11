import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import {
  listenToEditor,
  editorWindow,
  pushEditorTheme,
  inlineActionDescriptorsJson,
  runInlineAction,
  extensionContextMenuItemsJson,
  type EditorWindow
} from './editorBridge'
import { EditorToolbar, type FormattingState } from './EditorToolbar'
import { useEditorBridge } from '../../stores/editorBridgeStore'
import { editorPane, useProjectStore, type ProjectStateDto, type SceneTabRef } from '../../stores/projectStore'
import { useShellStore } from '../../stores/shellStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { useWikiStore } from '../../stores/wikiStore'
import { dispatchForwardedHotkey } from '../../shell/hotkeys'
import { EntityTypeDialog } from '../../shell/EntityTypeDialog'
import { AppendToEntityDialog } from '../../shell/AppendToEntityDialog'
import { InputDialog } from '../../shell/InputDialog'
import { rpc } from '../../rpc/client'
import { useEntityPeek, type PeekScope } from './PeekCard'
import './editor.css'

/**
 * The two hard bands of the readability ramp, as a wash rather than a fill -
 * the words still have to be readable through it. Read off the design tokens
 * because the editor is a separate document with no access to them, which also
 * means a theme that restates the ramp is honoured for free.
 */
function hardBandColors(): Record<string, string> {
  const style = getComputedStyle(document.documentElement)
  const wash = (token: string, percent: number): string =>
    `color-mix(in srgb, ${style.getPropertyValue(token).trim()} ${percent}%, transparent)`
  return {
    Difficult: wash('--nl-readability-difficult', 22),
    VeryDifficult: wash('--nl-readability-very-difficult', 30)
  }
}

function pushEditorSettings(editor: EditorWindow, initial = false): void {
  const view = useSettingsStore.getState().view
  if (!view) return
  const eff = view.effective
  editor.setFont(eff.editorFontFamily, eff.editorFontSize)
  editor.setReadingComfort(
    eff.editorLineHeight, eff.editorLetterSpacing, eff.editorParagraphSpacing)
  if (!initial || eff.readabilityHighlighting) {
    editor.setReadabilityEnabled(eff.readabilityHighlighting)
  }
  // Typewriter scroll makes no sense on a phone, so force it off on mobile even
  // if a (desktop) project has it enabled - the setting is also hidden there.
  const typewriter = window.novalist.isMobile === true ? false : eff.typewriterScrollEnabled
  // On the initial push, disabled toggles match editor.html's startup state;
  // skipping them avoids DOM rebuilds that would drop the caret mid-typing.
  if (!initial || typewriter) {
    editor.setTypewriterScroll(typewriter, eff.typewriterScrollAnchor)
  }
  if (!initial || eff.composeDimming) {
    editor.setComposeDimming(eff.composeDimming)
  }
  // The book's own completion list. Fetched rather than carried in settings:
  // it belongs to the book, not to the machine.
  void rpc
    .request<{ words: string[]; trigger: number }>('completion/get')
    .then((list) => editor.setCompletionList(list.words ?? [], list.trigger ?? 3))
    .catch(() => editor.setCompletionList([], 3))
  if (!initial || eff.pageViewEnabled) {
    editor.setPageView(eff.pageViewEnabled)
  }
  if (!initial || eff.enableBookParagraphSpacing) {
    editor.setBookParagraphSpacing(eff.enableBookParagraphSpacing)
  }
  if (!initial || eff.grammarCheckEnabled) {
    editor.setGrammarCheckEnabled(eff.grammarCheckEnabled)
  }
  if (!initial || eff.spellCheckEnabled) {
    editor.setSpellCheck(eff.spellCheckEnabled)
  }
  // The prose is checked against the language it is written in, not the one the
  // menus are in - a German novel written on an English install still wants a
  // German dictionary.
  editor.setLanguage(eff.autoReplacementLanguage)
}

// Speech verbs mirror DialogueCorrectionExtension.GetLanguageConfig so the
// in-editor dialogue-punctuation pass matches the desktop build exactly.
const DIALOGUE_VERBS_DE = [
  'sagte', 'fragte', 'rief', 'schrie', 'flüsterte', 'erwiderte', 'antwortete', 'murmelte',
  'brummte', 'zischte', 'seufzte', 'stöhnte', 'meinte', 'entgegnete', 'sprach', 'erklärte',
  'bemerkte', 'bat', 'flehte', 'knurrte', 'hauchte', 'jammerte', 'klagte', 'stotterte',
  'stammelte', 'schluchzte', 'keuchte', 'wimmerte', 'drängte', 'forderte', 'befahl', 'warnte',
  'mahnte', 'tröstete', 'beruhigte'
]
const DIALOGUE_VERBS_EN = [
  'said', 'asked', 'whispered', 'shouted', 'cried', 'replied', 'answered', 'murmured',
  'exclaimed', 'muttered', 'yelled', 'screamed', 'called', 'remarked', 'responded', 'explained',
  'stated', 'declared', 'added', 'continued', 'insisted', 'suggested', 'wondered', 'demanded',
  'pleaded', 'begged', 'stammered', 'stuttered', 'sobbed', 'groaned', 'sighed', 'breathed',
  'hissed', 'snapped', 'barked', 'growled', 'urged', 'warned', 'cautioned', 'consoled'
]

/** Ports DialogueCorrectionExtension.SerializeConfigJson to the client. */
function dialogueCorrectionConfigJson(language: string, enabled: boolean): string {
  if (!enabled) return JSON.stringify({ enabled: false })
  const ruleFamily = language === 'de-low' || language === 'de-guillemet' ? 'de' : 'en'
  const openQuote = language === 'de-low' ? '„' : language === 'de-guillemet' ? '»' : '“'
  const closeQuote = language === 'de-low' ? '“' : language === 'de-guillemet' ? '«' : '”'
  return JSON.stringify({
    enabled: true,
    ruleFamily,
    openQuote,
    closeQuote,
    speechVerbs: ruleFamily === 'de' ? DIALOGUE_VERBS_DE : DIALOGUE_VERBS_EN
  })
}

/**
 * Pushes the config the editor page needs beyond raw view settings:
 * auto-replacement pairs, dialogue-correction rules, localized context-menu
 * labels, and the (extension-contributed) inline-action list.
 */
function pushEditorConfig(editor: EditorWindow, t: TFunction): void {
  const view = useSettingsStore.getState().view
  if (!view) return
  const eff = view.effective
  // An empty pair list is the off switch the editor page already understands:
  // tryAutoReplace returns early and every keystroke stands as typed. The
  // stored pairs are left untouched, so switching back on restores them.
  const pairs = eff.autoReplacementEnabled
    ? ((view.overrides?.autoReplacements ?? view.global.autoReplacements) as unknown[] | undefined)
    : []
  editor.setAutoReplacements(JSON.stringify(pairs ?? []))
  editor.setDialogueCorrectionConfig(
    dialogueCorrectionConfigJson(eff.autoReplacementLanguage, eff.dialogueCorrectionEnabled)
  )
  editor.setContextMenuLabels(
    JSON.stringify({
      cut: t('editor.contextMenu.cut'),
      copy: t('editor.contextMenu.copy'),
      paste: t('editor.contextMenu.paste'),
      selectAll: t('editor.contextMenu.selectAll'),
      addComment: t('editor.contextMenu.addComment'),
      addFootnote: t('editor.contextMenu.addFootnote'),
      addToDictionary: t('editor.contextMenu.addToDictionary'),
      createEntity: t('editor.contextMenu.createEntity'),
      appendToEntity: t('editor.contextMenu.appendToEntity'),
      splitScene: t('editor.contextMenu.splitScene'),
      insertImage: t('editor.contextMenu.insertImage'),
      cutToDarlings: t('editor.contextMenu.cutToDarlings'),
      groupScene: t('editor.contextMenu.groupScene'),
      groupCodex: t('editor.contextMenu.groupCodex'),
      noSuggestions: t('editor.contextMenu.noSuggestions')
    })
  )
  editor.setMentionLabels(
    JSON.stringify({
      create: t('capture.mentionCreateRow'),
      noMatches: t('editor.mentionNoMatches')
    })
  )
  editor.setInlineActions(inlineActionDescriptorsJson())
  editor.setExtensionContextMenuItems(extensionContextMenuItemsJson())
}

const DEFAULT_FORMATTING: FormattingState = {
  bold: false,
  italic: false,
  underline: false,
  alignment: 'left',
  paragraphStyle: '',
  bulletList: false,
  numberList: false
}

interface SceneComment {
  id: string
  anchorText: string
  text: string
  resolved: boolean
}

interface SceneFootnote {
  id: string
  number: number
  text: string
}

/** Ordered tab strip for the scenes open in one editor pane. */
function SceneTabStrip({ paneId }: { paneId: string }): React.JSX.Element | null {
  const { t } = useTranslation()
  const tabs = useProjectStore((s) => editorPane(s, paneId).tabs)
  const activeId = useProjectStore((s) => editorPane(s, paneId).sceneId)
  const chapters = useProjectStore((s) => s.chapters)
  const dirtyMap = useProjectStore((s) => s.dirtyMap)
  const editorCount = useProjectStore(
    (s) => Object.values(s.editors).filter((e) => e.sceneId !== null).length
  )
  const [menu, setMenu] = useState<{ x: number; y: number; sceneId: string } | null>(null)

  // With a second editor open every pane shows its strip, so each side's scene
  // is closeable and the panes look alike. A lone editor keeps the strip-free
  // look until a second scene is opened in it.
  if (tabs.length === 0) return null
  if (editorCount < 2 && tabs.length <= 1) return null

  const titleFor = (ref: SceneTabRef): string => {
    const chapter = chapters.find((c) => c.guid === ref.chapterGuid)
    const scene = chapter?.scenes.find((s) => s.id === ref.sceneId)
    return scene?.title || chapter?.title || ''
  }

  const activate = (ref: SceneTabRef): void => {
    if (ref.sceneId === activeId) return
    void useProjectStore.getState().openSceneIn(paneId, ref.chapterGuid, ref.sceneId)
  }
  const close = (sceneId: string): void => {
    void useProjectStore.getState().closeTab(paneId, sceneId)
  }
  const moveOther = (sceneId: string): void => {
    setMenu(null)
    void useProjectStore.getState().moveTabToOtherPane(paneId, sceneId)
  }

  return (
    <div className="editor-tabs" role="tablist">
      {tabs.map((ref) => (
        <div
          key={ref.sceneId}
          className={`editor-tab${ref.sceneId === activeId ? ' active' : ''}`}
          role="tab"
          aria-selected={ref.sceneId === activeId}
          title={titleFor(ref)}
          onClick={() => activate(ref)}
          onAuxClick={(e) => {
            if (e.button === 1) {
              e.preventDefault()
              close(ref.sceneId)
            }
          }}
          onContextMenu={(e) => {
            e.preventDefault()
            setMenu({ x: e.clientX, y: e.clientY, sceneId: ref.sceneId })
          }}
        >
          {dirtyMap[ref.sceneId] && <span className="editor-tab-dirty" aria-hidden="true" />}
          <span className="editor-tab-title">{titleFor(ref)}</span>
          <button
            className="editor-tab-close"
            aria-label={t('editor.tabClose')}
            onClick={(e) => {
              e.stopPropagation()
              close(ref.sceneId)
            }}
          >
            ×
          </button>
        </div>
      ))}
      {menu && (
        <>
          <div
            className="editor-tab-menu-scrim"
            onClick={() => setMenu(null)}
            onContextMenu={(e) => {
              e.preventDefault()
              setMenu(null)
            }}
          />
          <div className="editor-tab-menu" style={{ left: menu.x, top: menu.y }}>
            <button
              onClick={() => {
                close(menu.sceneId)
                setMenu(null)
              }}
            >
              {t('editor.tabClose')}
            </button>
            <button onClick={() => moveOther(menu.sceneId)}>{t('editor.tabMoveOther')}</button>
          </div>
        </>
      )}
    </div>
  )
}

/**
 * Hosts editor.html (carried over from the Avalonia app unchanged apart from
 * the parent-frame transport branch) and wires the ready handshake, theme,
 * content push, and autosave round-trip.
 */
export function EditorFrame({ paneId }: { paneId?: string }): React.JSX.Element {
  const { t, i18n } = useTranslation()
  const iframeRef = useRef<HTMLIFrameElement>(null)
  const editorRef = useRef<EditorWindow | null>(null)
  const [linkPrompt, setLinkPrompt] = useState(false)
  // Mobile has no panes, so an editor mounted without one is whichever the
  // shell is following.
  const fallbackPaneId = useProjectStore((s) => s.activeEditorPaneId)
  const pane = paneId ?? fallbackPaneId ?? ''
  const openSceneId = useProjectStore((s) => editorPane(s, pane).sceneId)
  const sceneHtml = useProjectStore((s) => editorPane(s, pane).html)
  const isActiveEditor = useProjectStore((s) => s.activeEditorPaneId === pane)
  const chapters = useProjectStore((s) => s.chapters)
  const loadingRef = useRef(false)
  // The HTML the editor last reported to the store. The store round-trips every
  // keystroke back into sceneHtml, so without this the push effect below would
  // re-setContent on every keystroke - resetting the caret to the start and
  // wiping the native undo stack. We only push content the editor did NOT author.
  const lastReportedHtmlRef = useRef<string | null>(null)
  const [formatting, setFormatting] = useState<FormattingState>(DEFAULT_FORMATTING)
  const [speaking, setSpeaking] = useState(false)
  // An image waiting on its alt text. Asked for at insert time, because asking
  // later means never: a picture in the prose without one is invisible to a
  // reader using a screen reader and to an accessible export.
  const [pendingImage, setPendingImage] = useState<string | null>(null)
  // A name typed after `@` that matched no entity, waiting for the writer to pick
  // which kind of entity to create for it.
  const [pendingEntity, setPendingEntity] = useState<{ name: string; pendingId: string } | null>(
    null
  )
  // A selected passage on its way into a Codex entity's section.
  const [pendingAppend, setPendingAppend] = useState<string | null>(null)
  const annotationsRef = useRef<{ comments: SceneComment[]; footnotes: SceneFootnote[] }>({
    comments: [],
    footnotes: []
  })
  const entityIndexRef = useRef<
    Map<string, { id: string; name: string; detail: string; imagePath: string | null; type: string }>
  >(new Map())
  const openHoveredEntity = (entityType: string, entityId: string): void => {
    // A coined word has no Wiki article; it has a dictionary entry.
    if (entityType === 'conlang') {
      const word = conlangWordsRef.current.get(entityId)
      if (word) useShellStore.getState().navigateToLanguage(word)
      return
    }
    useShellStore.getState().setMainView('wiki')
    void useWikiStore.getState().openArticle(entityType, entityId)
  }

  /** Creates the entity the writer just named in the editor and upgrades the
   *  pending placeholder into a real mention. */
  const createPendingEntity = async (typeKey: string): Promise<void> => {
    const request = pendingEntity
    setPendingEntity(null)
    const editor = editorRef.current
    if (!request || !editor) return
    try {
      const record = await rpc.request<Record<string, unknown>>('entities/create', [
        typeKey,
        request.name,
        null
      ])
      editor.resolvePendingMention(request.pendingId, String(record.id), typeKey, request.name)
      // The new name has to become recognisable for hover cards and future @-picks.
      await pushEntityNames(editor)
    } catch {
      // Creation failed — leave the typed text in place rather than a placeholder.
      editor.resolvePendingMention(request.pendingId, null, null, request.name)
    }
  }

  /** Copies the selected passage into a Codex entity's section. */
  const appendSelectionToEntity = async (target: {
    typeKey: string
    id: string
    sectionTitle: string
  }): Promise<void> => {
    const passage = pendingAppend
    setPendingAppend(null)
    if (!passage) return
    await rpc.request('entities/appendToSection', [
      target.typeKey,
      target.id,
      target.sectionTitle,
      passage
    ])
  }

  const cancelPendingEntity = (): void => {
    const request = pendingEntity
    setPendingEntity(null)
    if (request) editorRef.current?.resolvePendingMention(request.pendingId, null, null, request.name)
  }

  // The pane's open chapter/scene, resolved from the live chapter list so a peek
  // over a character shows the values overridden for the scope currently in view.
  const scopeChapter = chapters.find((c) => c.scenes.some((s) => s.id === openSceneId))
  const peekScope: PeekScope = {
    chapterGuid: scopeChapter?.guid ?? null,
    chapterTitle: scopeChapter?.title ?? null,
    sceneTitle: scopeChapter?.scenes.find((s) => s.id === openSceneId)?.title ?? null,
    sceneId: openSceneId
  }
  // Shared focus-peek overlay: owns the show/hide debounce, the pointer-over-card
  // guard, pin state, and viewport-clamped positioning. Driven here by the iframe's
  // entity hover/exit messages; the context sidebar drives the same hook itself.
  /** Coined word id -> the word, so clicking a peek can search for it. */
  const conlangWordsRef = useRef(new Map<string, string>())

  const peek = useEntityPeek({ scope: peekScope, onOpen: openHoveredEntity })
  // Read the latest controls from inside the (rarely re-created) listener effect.
  const peekRef = useRef(peek)
  peekRef.current = peek

  // Same reason: the listener outlives a scene switch, and a cut filed under
  // the scene the writer was in twenty minutes ago is a cut they cannot place.
  const sceneTitleRef = useRef(peekScope.sceneTitle)
  sceneTitleRef.current = peekScope.sceneTitle

  const pushEntityNames = async (editor: EditorWindow): Promise<void> => {
    type Hit = { id: string; name: string; detail: string; imagePath: string | null; type: string }
    // Per-entry rules from the Codex. Absent means the entry uses the defaults,
    // which is what every project had before these controls existed.
    type Match = {
      caseSensitive: boolean
      matchPlurals: boolean
      exclusions: string[]
      ignoredSceneIds: string[]
      plurals: string[]
    }
    const index = new Map<string, Hit>()
    // Collect every matchable text -> candidate(s). Names that resolve to more
    // than one entity are dropped (mirrors FocusPeekExtension's Count==1 rule)
    // so an ambiguous first name never peeks the wrong character.
    const byText = new Map<
      string,
      { hit: Hit; primaryName: string; isAlias: boolean; text: string; match: Match | null }[]
    >()
    const addText = (
      text: string,
      hit: Hit,
      primaryName: string,
      isAlias: boolean,
      match: Match | null
    ): void => {
      const trimmed = text.trim()
      const key = trimmed.toLowerCase()
      if (!key) return
      const list = byText.get(key) ?? []
      list.push({ hit, primaryName, isAlias, text: trimmed, match })
      byText.set(key, list)
    }
    const sceneId = editorPane(useProjectStore.getState(), pane).sceneId
    for (const type of ['character', 'location', 'item', 'lore']) {
      const list = await rpc.request<
        {
          id: string
          name: string
          detail: string
          imagePath: string | null
          aliases: string[]
          firstName: string | null
          match: Match | null
        }[]
      >('entities/list', [type])
      for (const entity of list) {
        const match = entity.match ?? null
        // Silenced for this scene: the entry stays in the Codex, it just stops
        // being detected here.
        if (sceneId && match?.ignoredSceneIds.includes(sceneId)) continue
        const hit: Hit = { ...entity, type }
        index.set(entity.id, hit)
        addText(entity.name, hit, entity.name, false, match)
        if (entity.firstName) addText(entity.firstName, hit, entity.name, true, match)
        for (const alias of entity.aliases ?? []) addText(alias, hit, entity.name, true, match)
        // Plural forms come precomputed from the backend so the renderer never
        // has to know a language's plural rules.
        for (const plural of match?.plurals ?? []) addText(plural, hit, entity.name, true, match)
      }
    }
    const names: {
      name: string
      entityId: string
      entityType: string
      isAlias: boolean
      caseSensitive?: boolean
      exclusions?: string[]
    }[] = []
    const candidates: {
      entityId: string
      entityType: string
      primaryName: string
      matchedText: string
      isAlias: boolean
      subtitle: string
    }[] = []
    for (const [key, list] of byText) {
      if (list.length !== 1) continue // ambiguous — drop, like the desktop app
      const { hit, primaryName, isAlias, text, match } = list[0]
      index.set(key, hit)
      names.push({
        name: text,
        entityId: hit.id,
        entityType: hit.type,
        isAlias,
        caseSensitive: match?.caseSensitive,
        exclusions: match?.exclusions
      })
      candidates.push({
        entityId: hit.id,
        entityType: hit.type,
        primaryName,
        matchedText: text,
        isAlias,
        subtitle: hit.detail ?? ''
      })
    }
    // Words the writer coined, so a language module is something that helps
    // while drafting rather than a list they have to go and open. Hovering one
    // raises the same card an entity name does; the backend answers for it.
    try {
      type Language = { id: string; name: string; words: { id: string; word: string }[] }
      const languages = await rpc.request<Language[]>('conlang/list')
      const seen = new Set<string>()
      const words = new Map<string, string>()
      for (const language of languages ?? []) {
        for (const word of language.words ?? []) {
          const text = (word.word ?? '').trim()
          // Two letters is where an invented word stops being distinguishable
          // from a preposition in the prose language, and highlighting every
          // "an" in the manuscript is worse than not highlighting anything.
          if (text.length < 3) continue
          const key = text.toLowerCase()
          // A word coined twice, or one that collides with a name already
          // matched, is left alone rather than pointing somewhere arbitrary -
          // the same Count==1 rule the entity names follow.
          if (seen.has(key) || byText.has(key)) continue
          seen.add(key)
          words.set(word.id, text)
          names.push({ name: text, entityId: word.id, entityType: 'conlang', isAlias: false })
        }
      }
      conlangWordsRef.current = words
    } catch {
      // A project with no languages, or an older backend: the manuscript simply
      // highlights nothing extra.
    }

    entityIndexRef.current = index
    editor.setEntityNames(JSON.stringify(names))
    // Same records feed the @-mention autocomplete picker.
    editor.setMentionCandidates(JSON.stringify(candidates))
  }

  const pushAnnotations = (editor: EditorWindow): void => {
    editor.setCommentsData(
      annotationsRef.current.comments.map((c) => ({
        id: c.id,
        anchorText: c.anchorText,
        text: c.text
      }))
    )
    editor.setFootnotesData(
      annotationsRef.current.footnotes.map((f) => ({ id: f.id, text: f.text }))
    )
  }

  const paneIds = (): { chapterGuid: string | null; sceneId: string | null } => {
    const editor = editorPane(useProjectStore.getState(), pane)
    return { chapterGuid: editor.chapterGuid, sceneId: editor.sceneId }
  }

  const loadAnnotations = async (editor: EditorWindow | null): Promise<void> => {
    const { chapterGuid, sceneId } = paneIds()
    if (!chapterGuid || !sceneId) return
    const annotations = await rpc.request<{ comments: SceneComment[]; footnotes: SceneFootnote[] }>(
      'scenes/getAnnotations',
      [chapterGuid, sceneId]
    )
    annotationsRef.current = annotations
    if (editor) pushAnnotations(editor)
  }

  const persistAnnotations = (): void => {
    const { chapterGuid, sceneId } = paneIds()
    if (!chapterGuid || !sceneId) return
    void rpc.request('scenes/setAnnotations', [
      chapterGuid,
      sceneId,
      annotationsRef.current.comments,
      annotationsRef.current.footnotes
    ])
  }

  // Push content on scene switch and on genuine external changes (snapshot
  // restore, live disk edits), but NOT when sceneHtml is merely the echo of the
  // edit the editor just reported - that would reset the caret and kill undo.
  useEffect(() => {
    const editor = editorRef.current
    if (!editor || sceneHtml === null) return
    if (sceneHtml === lastReportedHtmlRef.current) return
    loadingRef.current = true
    editor.setContent(sceneHtml)
    loadingRef.current = false
    lastReportedHtmlRef.current = sceneHtml
    void loadAnnotations(editor)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openSceneId, sceneHtml])

  // Detection is scene-scoped: an entry silenced in one scene must come back in
  // the next, so the name list is rebuilt whenever the open scene changes.
  useEffect(() => {
    const editor = editorRef.current
    if (!editor || !openSceneId) return
    void pushEntityNames(editor)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openSceneId])

  useEffect(() => {
    const iframe = iframeRef.current
    if (!iframe) return

    const showHoverCard = (
      hit: { id: string; type: string },
      x: number,
      y: number
    ): void => {
      if (!iframeRef.current) return
      // Translate the iframe-relative point into viewport coordinates; the shared
      // overlay handles the pinned guard, debounce cancel, and clamping.
      const rect = iframeRef.current.getBoundingClientRect()
      peekRef.current.showAt(
        { entityType: hit.type, entityId: hit.id },
        rect.left + x,
        rect.top + y
      )
    }

    const dispose = listenToEditor(iframe, (message) => {
      const editor = editorRef.current
      switch (message.type) {
        case 'ready': {
          // Where a stored image path hangs off, so the frame can show one.
          void rpc
            .request<string>('gallery/base')
            .then((base) => editorRef.current?.setImageBase(`novalist-project://nl/${base}`))
            .catch(() => undefined)
          const live = editorWindow(iframe)
          if (!live) return
          editorRef.current = live
          pushEditorTheme(live)
          live.setLanguage(i18n.language.startsWith('de') ? 'de' : 'en')
          // Mobile: full-width text (no 18em comment gutter) + touch-sized toolbar.
          live.setMobile(window.novalist.isMobile === true)
          // Content loads synchronously so typing can never race a deferred
          // setContent; the settings push is made non-destructive instead.
          const initialHtml = editorPane(useProjectStore.getState(), pane).html
          if (initialHtml !== null) {
            loadingRef.current = true
            live.setContent(initialHtml)
            loadingRef.current = false
            lastReportedHtmlRef.current = initialHtml
          }
          void loadAnnotations(live)
          void pushEntityNames(live)
          const settings = useSettingsStore.getState()
          if (settings.view) {
            pushEditorSettings(live, true)
            pushEditorConfig(live, t)
          } else {
            void settings.load().then(() => {
              if (editorRef.current) {
                pushEditorSettings(editorRef.current, true)
                pushEditorConfig(editorRef.current, t)
              }
            })
          }
          break
        }
        case 'contentChanged': {
          if (loadingRef.current || !editor) return
          // Record what the editor authored so the push effect treats the
          // store's echo of this same HTML as a no-op (keeps caret + undo).
          lastReportedHtmlRef.current = String(message.html ?? '')
          useProjectStore
            .getState()
            .onEditorContentChanged(
              pane,
              String(message.html ?? ''),
              String(message.plainText ?? '')
            )
          break
        }
        case 'grammarCheckRequest': {
          if (!editor) return
          void rpc
            .request<unknown[]>('grammar/check', [String(message.plainText ?? '')])
            .then((issues) => {
              editorRef.current?.setGrammarIssues(JSON.stringify(issues))
            })
            .catch(() => {
              // Offline or endpoint unavailable: clear underlines quietly.
              editorRef.current?.setGrammarIssues('[]')
            })
          break
        }
        case 'insertImageRequested': {
          void (async () => {
            const path = await window.novalist.pickFile(t('editorImage.pick'), 'images')
            if (!path) return
            const image = await rpc.request<{ path: string; url: string }>('gallery/import', [path])
            setPendingImage(image.path)
          })()
          break
        }
        case 'replaceMisspelling': {
          window.novalist.replaceMisspelling(String(message.replacement ?? ''))
          break
        }
        case 'keepDarling': {
          // Nothing is logged on this path at any level: the payload is the
          // writer's prose.
          void rpc.request('darlings/keep', [
            String(message.text ?? ''),
            sceneTitleRef.current ?? ''
          ])
          break
        }
        case 'requestLink': {
          // The frame owns no dialogs, so it asks and the host answers. An
          // empty address unlinks, which is how a link is removed.
          setLinkPrompt(true)
          break
        }
        case 'requestAddComment': {
          editorRef.current?.addCommentToSelection(crypto.randomUUID())
          break
        }
        case 'commentAdded': {
          annotationsRef.current.comments.push({
            id: String(message.commentId),
            anchorText: String(message.anchorText ?? ''),
            text: '',
            resolved: false
          })
          persistAnnotations()
          if (editorRef.current) pushAnnotations(editorRef.current)
          break
        }
        case 'commentTextChanged': {
          const comment = annotationsRef.current.comments.find(
            (c) => c.id === String(message.commentId)
          )
          if (comment) {
            comment.text = String(message.text ?? '')
            persistAnnotations()
          }
          break
        }
        case 'commentDeleted': {
          annotationsRef.current.comments = annotationsRef.current.comments.filter(
            (c) => c.id !== String(message.commentId)
          )
          persistAnnotations()
          break
        }
        case 'commentClicked': {
          editorRef.current?.scrollToCommentById(String(message.commentId ?? ''))
          break
        }
        case 'requestAddFootnote': {
          editorRef.current?.insertFootnoteAtSelection(crypto.randomUUID())
          break
        }
        case 'footnoteInserted': {
          annotationsRef.current.footnotes.push({
            id: String(message.footnoteId),
            number: Number(message.number ?? annotationsRef.current.footnotes.length + 1),
            text: ''
          })
          persistAnnotations()
          break
        }
        case 'splitSceneRequested': {
          // Everything after the caret becomes a new scene right below this
          // one, carrying the date, stage, plotlines and POV that still
          // describe it.
          const { chapterGuid, sceneId } = paneIds()
          if (!chapterGuid || !sceneId) break
          void rpc
            .request<{ sceneId: string | null; state: ProjectStateDto }>('sceneSplit/split', [
              chapterGuid,
              sceneId,
              String(message.before ?? ''),
              String(message.after ?? ''),
              null
            ])
            .then((result) => {
              useProjectStore.getState().applyState(result.state)
              // The scene shrank under the editor, so it has to be re-read
              // rather than left showing both halves.
              void useProjectStore.getState().openSceneIn(pane, chapterGuid, sceneId)
            })
          break
        }
        case 'inlineActionRequested': {
          const actionId = String(message.actionId ?? '')
          const selected = String(message.selectedText ?? '')
          // Carried so an action invoked at a bare caret has something to work
          // from: the prose before it, and whatever the writer typed after the
          // slash as a directive.
          void runInlineAction(actionId, selected, {
            precedingText: String(message.precedingText ?? ''),
            directive: String(message.directive ?? '')
          }).then((result) => {
            editorRef.current?.applyInlineActionResult(JSON.stringify({ actionId, ...result }))
          })
          break
        }
        case 'extensionContextMenuRequested': {
          const itemId = String(message.itemId ?? '')
          const proj = useProjectStore.getState()
          void rpc.request('extensions/contextMenuItem/execute', [
            itemId,
            proj.openChapterGuid ?? '',
            proj.openSceneId ?? ''
          ])
          break
        }
        case 'hotkey': {
          const key = String(message.key ?? '')
          const ran = dispatchForwardedHotkey({
            key,
            code: String(message.code ?? ''),
            ctrlKey: Boolean(message.ctrlKey),
            metaKey: Boolean(message.metaKey),
            shiftKey: Boolean(message.shiftKey),
            altKey: Boolean(message.altKey)
          })
          // Escape is not a registered hotkey - it is what dismisses whatever
          // overlay is up (mobile sheets, dialogs). Those listen on the window,
          // which an iframe keydown never reaches, so replay it there.
          if (!ran && key === 'Escape') {
            window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
          }
          break
        }
        case 'zoom': {
          const settings = useSettingsStore.getState()
          const view = settings.view
          if (!view) break
          const current = view.effective.editorFontSize
          const next = Math.min(36, Math.max(8, current + Number(message.delta ?? 0)))
          if (next === current) break
          const scope =
            view.overrides && view.overrides.editorFontSize != null ? 'project' : 'global'
          void settings.update(scope, { editorFontSize: next })
          break
        }
        case 'appendToEntityRequested': {
          setPendingAppend(String(message.text ?? ''))
          break
        }
        case 'mentionCreateRequested': {
          // A name typed after `@` that matched nothing: ask which kind of entity
          // to make, then swap the placeholder for a real mention.
          setPendingEntity({
            name: String(message.name ?? ''),
            pendingId: String(message.pendingId ?? '')
          })
          break
        }
        case 'entityMentionHover': {
          const hit = entityIndexRef.current.get(String(message.entityId))
          if (hit) showHoverCard(hit, Number(message.x ?? 0), Number(message.y ?? 0))
          break
        }
        case 'entityHover': {
          const hit = entityIndexRef.current.get(String(message.alias ?? '').toLowerCase())
          if (hit) showHoverCard(hit, Number(message.x ?? 0), Number(message.y ?? 0))
          break
        }
        case 'entityExit': {
          // Debounced so moving the pointer onto the card doesn't dismiss it.
          peekRef.current.scheduleHide()
          break
        }
        case 'pointerPressed': {
          // A click in the editor dismisses the card unless it is pinned.
          peekRef.current.hide()
          break
        }
        case 'addToDictionary': {
          void rpc.request<boolean>('grammar/addToDictionary', [String(message.word ?? '')])
          break
        }
        case 'readabilityRequest': {
          void rpc
            .request<{ level: string }[]>('style/sentenceReadability', [
              String(message.plainText ?? '')
            ])
            .then((sentences) => {
              // Only the two hard bands are painted. Tinting every sentence
              // turns the page into a heat map you stop reading; what a writer
              // needs is the handful of sentences that fight the reader.
              const colors = hardBandColors()
              editorRef.current?.setReadability(
                JSON.stringify({
                  sentences: sentences.filter((s) => s.level in colors),
                  colors
                })
              )
            })
            .catch(() => editorRef.current?.setReadability('{"sentences":[]}'))
          break
        }
        case 'speakSentence': {
          // One sentence at a time so the editor keeps highlighting the one
          // being read; it waits for this reply before moving on.
          const frame = editorRef.current
          void rpc
            .request<boolean>('voices/speak', [
              String(message.text ?? ''),
              (message.voiceId as string) || null,
              Number(message.rate) || 1
            ])
            .then((ok) => frame?.onSentenceSpoken(ok))
            .catch(() => frame?.onSentenceSpoken(false))
          break
        }
        case 'stopSystemSpeech': {
          void rpc.request('voices/stop').catch(() => {})
          break
        }
        case 'readAloudStateChanged': {
          setSpeaking(Boolean(message.speaking))
          break
        }
        case 'formattingChanged': {
          setFormatting({
            bold: Boolean(message.bold),
            italic: Boolean(message.italic),
            underline: Boolean(message.underline),
            alignment: (message.alignment as FormattingState['alignment']) ?? 'left',
            paragraphStyle: String(message.paragraphStyle ?? ''),
            bulletList: Boolean(message.bulletList),
            numberList: Boolean(message.numberList)
          })
          break
        }
        default:
          break
      }
    })

    // Re-push theme when the OS/light-dark theme flips under the editor.
    const observer = new MutationObserver(() => {
      if (editorRef.current) pushEditorTheme(editorRef.current)
    })
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })

    // Live-apply settings changes (font, typewriter, page view, config) to the editor.
    const unsubscribeSettings = useSettingsStore.subscribe(() => {
      if (editorRef.current) {
        pushEditorSettings(editorRef.current)
        pushEditorConfig(editorRef.current, t)
      }
    })

    // If the effect re-runs after the iframe already booted (e.g. a language
    // change re-created this closure), re-acquire the live editor window —
    // 'ready' only fires once per page load.
    const existing = editorWindow(iframe)
    if (existing && typeof existing.setContent === 'function') {
      editorRef.current = existing
      existing.setLanguage(i18n.language.startsWith('de') ? 'de' : 'en')
    }

    return () => {
      dispose()
      observer.disconnect()
      unsubscribeSettings()
      peekRef.current.clearHide()
      editorRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [i18n.language])

  // Chromium reports the misspelling under the pointer as the menu opens, and
  // it is the only place those suggestions exist - there is no API to ask for
  // them. Handed to the frame so they land in the menu already on screen.
  useEffect(() => {
    window.novalist.onSpellingContext((word, suggestions) => {
      editorRef.current?.setSpellingSuggestions(word, suggestions)
    })
  }, [])

  // Panels outside this pane - the footnotes and comments lists - need a way
  // to reach the prose. Registered by the pane the writer is in alone: another
  // editor is showing a different scene, and answering for it would strip a
  // marker out of the wrong one.
  useEffect(() => {
    if (!isActiveEditor) return
    useEditorBridge.getState().register(editorRef.current, openSceneId)
    return () => useEditorBridge.getState().register(null, null)
  }, [isActiveEditor, openSceneId, sceneHtml])

  // Opening a scene puts the caret in it. Without this the writer clicks a
  // scene in the binder, starts typing, and the keystrokes go to the binder -
  // which is why the editor grew a focusEditor that nothing ever called.
  useEffect(() => {
    if (!isActiveEditor || !openSceneId) return
    const at = window.setTimeout(() => {
      // The iframe first. Focusing an element inside a frame does nothing for
      // the keyboard while the frame itself is not focused - the caret appears
      // and the typing still goes to whatever the host had focused, which is
      // why calling focusEditor on its own looked like it did nothing.
      iframeRef.current?.focus()
      editorRef.current?.focusEditor()
    }, 60)
    return () => window.clearTimeout(at)
  }, [pane, openSceneId])

  return (
    <div className="editor-pane">
      <SceneTabStrip paneId={pane} />
      <EditorToolbar
        formatting={formatting}
        editor={() => editorRef.current}
        speaking={speaking}
        onToggleReadAloud={() => {
          const live = editorRef.current
          if (!live) return
          if (speaking) {
            live.stopReadAloud()
            return
          }
          const eff = useSettingsStore.getState().view?.effective
          live.startReadAloud(true, eff?.readAloudRate ?? 1, eff?.readAloudVoiceUri ?? null)
        }}
      />
      <iframe
        ref={iframeRef}
        className="editor-frame"
        src="./editor/editor.html"
        title="editor"
        sandbox="allow-scripts allow-same-origin"
      />
      {peek.overlay}
      {pendingEntity && (
        <EntityTypeDialog
          name={pendingEntity.name}
          onPick={(typeKey) => void createPendingEntity(typeKey)}
          onCancel={cancelPendingEntity}
        />
      )}
      {pendingAppend != null && (
        <AppendToEntityDialog
          text={pendingAppend}
          onConfirm={(target) => void appendSelectionToEntity(target)}
          onCancel={() => setPendingAppend(null)}
        />
      )}
      {/* The frame owns no dialogs, so it asks and the host answers. An empty
          address unlinks, which is how a link is taken off again. */}
      {linkPrompt && (
        <InputDialog
          title={t('editor.linkPrompt')}
          placeholder="https://"
          onCancel={() => setLinkPrompt(false)}
          onSubmit={(value) => {
            setLinkPrompt(false)
            editorRef.current?.applyLink(value.trim())
          }}
        />
      )}

      {/* Alt text, asked for at insert time. Asking later means never, and a
          picture nobody described is invisible to a reader who cannot see it. */}
      {pendingImage && (
        <InputDialog
          title={t('editorImage.altTitle')}
          placeholder={t('editorImage.altPlaceholder')}
          onCancel={() => {
            // Cancelling still places the image; a writer who does not want to
            // describe it should not lose the insert over it.
            const path = pendingImage
            setPendingImage(null)
            editorRef.current?.insertImageAtCaret(path, '')
          }}
          onSubmit={(value) => {
            const path = pendingImage
            setPendingImage(null)
            editorRef.current?.insertImageAtCaret(path, value.trim())
          }}
        />
      )}
    </div>
  )
}
