import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import {
  listenToEditor,
  editorWindow,
  pushEditorTheme,
  inlineActionDescriptorsJson,
  runInlineAction,
  type EditorWindow
} from './editorBridge'
import { EditorToolbar, type FormattingState } from './EditorToolbar'
import { useProjectStore, type SceneTabRef, type EditorPane } from '../../stores/projectStore'
import { useShellStore } from '../../stores/shellStore'
import { useSettingsStore } from '../../stores/settingsStore'
import { useCodexStore } from '../../stores/codexStore'
import { dispatchForwardedHotkey } from '../../shell/hotkeys'
import { rpc } from '../../rpc/client'
import './editor.css'

function pushEditorSettings(editor: EditorWindow, initial = false): void {
  const view = useSettingsStore.getState().view
  if (!view) return
  const eff = view.effective
  editor.setFont(eff.editorFontFamily, eff.editorFontSize)
  // On the initial push, disabled toggles match editor.html's startup state;
  // skipping them avoids DOM rebuilds that would drop the caret mid-typing.
  if (!initial || eff.typewriterScrollEnabled) {
    editor.setTypewriterScroll(eff.typewriterScrollEnabled, eff.typewriterScrollAnchor)
  }
  if (!initial || eff.pageViewEnabled) {
    editor.setPageView(eff.pageViewEnabled)
  }
  if (!initial || eff.enableBookParagraphSpacing) {
    editor.setBookParagraphSpacing(eff.enableBookParagraphSpacing)
  }
  if (!initial || eff.grammarCheckEnabled) {
    editor.setGrammarCheckEnabled(eff.grammarCheckEnabled)
  }
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
  const pairs = (view.overrides?.autoReplacements ?? view.global.autoReplacements) as unknown[] | undefined
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
      addToDictionary: t('editor.contextMenu.addToDictionary')
    })
  )
  editor.setInlineActions(inlineActionDescriptorsJson())
}

/** A couple of value chips per entity type, mirroring FocusPeek pills. */
function extractPeekChips(type: string, record: Record<string, unknown>): string[] {
  const field = (key: string): string => {
    const value = record[key]
    return typeof value === 'string' ? value.trim() : ''
  }
  const keys =
    type === 'character'
      ? ['role', 'gender', 'age']
      : type === 'location'
        ? ['type', 'parent']
        : type === 'item'
          ? ['type', 'origin']
          : type === 'lore'
            ? ['category']
            : []
  return keys.map(field).filter((v) => v.length > 0).slice(0, 3)
}

const DEFAULT_FORMATTING: FormattingState = {
  bold: false,
  italic: false,
  underline: false,
  alignment: 'left'
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

interface HoverCard {
  x: number
  y: number
  name: string
  detail: string
  imagePath: string | null
  typeLabel: string
  entityType: string
  entityId: string
  chips: string[]
  relationships: string[]
  sections: string[]
}

/** Ordered tab strip for the scenes open in one editor pane. */
function SceneTabStrip({ pane }: { pane: EditorPane }): React.JSX.Element | null {
  const { t } = useTranslation()
  const tabs = useProjectStore((s) => (pane === 'split' ? s.splitTabs : s.openTabs))
  const activeId = useProjectStore((s) => (pane === 'split' ? s.splitSceneId : s.openSceneId))
  const chapters = useProjectStore((s) => s.chapters)
  const dirtyMap = useProjectStore((s) => s.dirtyMap)
  const [menu, setMenu] = useState<{ x: number; y: number; sceneId: string } | null>(null)

  // A single open scene keeps the original strip-free look.
  if (tabs.length <= 1) return null

  const titleFor = (ref: SceneTabRef): string => {
    const chapter = chapters.find((c) => c.guid === ref.chapterGuid)
    const scene = chapter?.scenes.find((s) => s.id === ref.sceneId)
    return scene?.title || chapter?.title || ''
  }

  const activate = (ref: SceneTabRef): void => {
    if (ref.sceneId === activeId) return
    const store = useProjectStore.getState()
    void (pane === 'split'
      ? store.openSceneInSplit(ref.chapterGuid, ref.sceneId)
      : store.openScene(ref.chapterGuid, ref.sceneId))
  }
  const close = (sceneId: string): void => {
    void useProjectStore.getState().closeTab(pane, sceneId)
  }
  const moveOther = (sceneId: string): void => {
    setMenu(null)
    void useProjectStore.getState().moveTabToOtherPane(pane, sceneId)
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
export function EditorFrame({ pane = 'primary' }: { pane?: EditorPane }): React.JSX.Element {
  const { t, i18n } = useTranslation()
  const iframeRef = useRef<HTMLIFrameElement>(null)
  const editorRef = useRef<EditorWindow | null>(null)
  const openSceneId = useProjectStore((s) => (pane === 'split' ? s.splitSceneId : s.openSceneId))
  const sceneHtml = useProjectStore((s) => (pane === 'split' ? s.splitSceneHtml : s.openSceneHtml))
  const loadingRef = useRef(false)
  const [formatting, setFormatting] = useState<FormattingState>(DEFAULT_FORMATTING)
  const annotationsRef = useRef<{ comments: SceneComment[]; footnotes: SceneFootnote[] }>({
    comments: [],
    footnotes: []
  })
  const entityIndexRef = useRef<
    Map<string, { id: string; name: string; detail: string; imagePath: string | null; type: string }>
  >(new Map())
  const [hoverCard, setHoverCard] = useState<HoverCard | null>(null)
  const hoverHideRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const clearHoverHide = (): void => {
    if (hoverHideRef.current) {
      clearTimeout(hoverHideRef.current)
      hoverHideRef.current = null
    }
  }
  const scheduleHoverHide = (): void => {
    clearHoverHide()
    hoverHideRef.current = setTimeout(() => setHoverCard(null), 220)
  }
  const openHoveredEntity = (entityType: string, entityId: string): void => {
    clearHoverHide()
    setHoverCard(null)
    useShellStore.getState().setMainView('codex')
    void useCodexStore
      .getState()
      .setType(entityType)
      .then(() => useCodexStore.getState().select(entityId))
  }

  const pushEntityNames = async (editor: EditorWindow): Promise<void> => {
    const index = new Map<
      string,
      { id: string; name: string; detail: string; imagePath: string | null; type: string }
    >()
    const names: { name: string; entityId: string; entityType: string; isAlias: boolean }[] = []
    const candidates: {
      entityId: string
      entityType: string
      primaryName: string
      matchedText: string
      isAlias: boolean
      subtitle: string
    }[] = []
    for (const type of ['character', 'location', 'item', 'lore']) {
      const list = await rpc.request<
        { id: string; name: string; detail: string; imagePath: string | null; aliases: string[] }[]
      >('entities/list', [type])
      for (const entity of list) {
        index.set(entity.id, { ...entity, type })
        index.set(entity.name.toLowerCase(), { ...entity, type })
        names.push({ name: entity.name, entityId: entity.id, entityType: type, isAlias: false })
        candidates.push({
          entityId: entity.id,
          entityType: type,
          primaryName: entity.name,
          matchedText: entity.name,
          isAlias: false,
          subtitle: entity.detail ?? ''
        })
        for (const alias of entity.aliases ?? []) {
          if (!alias.trim()) continue
          index.set(alias.toLowerCase(), { ...entity, type })
          names.push({ name: alias, entityId: entity.id, entityType: type, isAlias: true })
          candidates.push({
            entityId: entity.id,
            entityType: type,
            primaryName: entity.name,
            matchedText: alias,
            isAlias: true,
            subtitle: entity.detail ?? ''
          })
        }
      }
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
    const state = useProjectStore.getState()
    return pane === 'split'
      ? { chapterGuid: state.splitChapterGuid, sceneId: state.splitSceneId }
      : { chapterGuid: state.openChapterGuid, sceneId: state.openSceneId }
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

  // Push content whenever the open scene changes and the editor is live.
  useEffect(() => {
    const editor = editorRef.current
    if (!editor || sceneHtml === null) return
    loadingRef.current = true
    editor.setContent(sceneHtml)
    loadingRef.current = false
    void loadAnnotations(editor)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openSceneId, sceneHtml])

  useEffect(() => {
    const iframe = iframeRef.current
    if (!iframe) return

    const showHoverCard = (
      hit: { id: string; name: string; detail: string; imagePath: string | null; type: string },
      x: number,
      y: number
    ): void => {
      if (!iframeRef.current) return
      clearHoverHide()
      const rect = iframeRef.current.getBoundingClientRect()
      const typeLabel = t(`focusPeek.type${hit.type.charAt(0).toUpperCase()}${hit.type.slice(1)}`)
      setHoverCard({
        x: rect.left + x,
        y: rect.top + y,
        name: hit.name,
        detail: hit.detail,
        imagePath: hit.imagePath,
        typeLabel,
        entityType: hit.type,
        entityId: hit.id,
        chips: [],
        relationships: [],
        sections: []
      })
      // Enrich with a couple of attribute chips from the full record.
      void rpc
        .request<Record<string, unknown>>('entities/get', [hit.type, hit.id])
        .then((record) => {
          const chips = extractPeekChips(hit.type, record)
          const rels = Array.isArray(record.relationships)
            ? (record.relationships as { role: string; target: string }[])
                .filter((r) => r.role || r.target)
                .slice(0, 4)
                .map((r) => `${r.role}: ${r.target}`)
            : []
          const sections = Array.isArray(record.sections)
            ? (record.sections as { title: string }[]).map((sec) => sec.title).filter(Boolean).slice(0, 4)
            : []
          setHoverCard((prev) =>
            prev && prev.entityId === hit.id ? { ...prev, chips, relationships: rels, sections } : prev
          )
        })
        .catch(() => {
          // Record fetch is best-effort; the basic card still shows.
        })
    }

    const dispose = listenToEditor(iframe, (message) => {
      const editor = editorRef.current
      switch (message.type) {
        case 'ready': {
          const live = editorWindow(iframe)
          if (!live) return
          editorRef.current = live
          pushEditorTheme(live)
          live.setLanguage(i18n.language.startsWith('de') ? 'de' : 'en')
          // Content loads synchronously so typing can never race a deferred
          // setContent; the settings push is made non-destructive instead.
          const state = useProjectStore.getState()
          const initialHtml = pane === 'split' ? state.splitSceneHtml : state.openSceneHtml
          if (initialHtml !== null) {
            loadingRef.current = true
            live.setContent(initialHtml)
            loadingRef.current = false
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
          useProjectStore
            .getState()
            [pane === 'split' ? 'onSplitContentChanged' : 'onEditorContentChanged'](
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
        case 'inlineActionRequested': {
          const actionId = String(message.actionId ?? '')
          const selected = String(message.selectedText ?? '')
          void runInlineAction(actionId, selected).then((result) => {
            editorRef.current?.applyInlineActionResult(JSON.stringify({ actionId, ...result }))
          })
          break
        }
        case 'hotkey': {
          dispatchForwardedHotkey({
            key: String(message.key ?? ''),
            code: String(message.code ?? ''),
            ctrlKey: Boolean(message.ctrlKey),
            shiftKey: Boolean(message.shiftKey),
            altKey: Boolean(message.altKey)
          })
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
          scheduleHoverHide()
          break
        }
        case 'pointerPressed': {
          clearHoverHide()
          setHoverCard(null)
          break
        }
        case 'addToDictionary': {
          void rpc.request<boolean>('grammar/addToDictionary', [String(message.word ?? '')])
          break
        }
        case 'formattingChanged': {
          setFormatting({
            bold: Boolean(message.bold),
            italic: Boolean(message.italic),
            underline: Boolean(message.underline),
            alignment: (message.alignment as FormattingState['alignment']) ?? 'left'
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
      clearHoverHide()
      editorRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [i18n.language])

  return (
    <div className="editor-pane">
      <SceneTabStrip pane={pane} />
      <EditorToolbar formatting={formatting} editor={() => editorRef.current} />
      <iframe
        ref={iframeRef}
        className="editor-frame"
        src="./editor/editor.html"
        title="editor"
        sandbox="allow-scripts allow-same-origin"
      />
      {hoverCard && (
        <div
          className="editor-peek-card"
          style={{
            left: Math.min(hoverCard.x, window.innerWidth - 280),
            top: Math.min(hoverCard.y + 18, window.innerHeight - 180)
          }}
          onMouseEnter={clearHoverHide}
          onMouseLeave={() => setHoverCard(null)}
        >
          {hoverCard.imagePath && (
            <img src={`novalist-project://nl/${encodeURI(hoverCard.imagePath)}`} alt="" />
          )}
          <div className="editor-peek-body">
            <div className="editor-peek-head">
              <span className="editor-peek-name">{hoverCard.name}</span>
              <span className="editor-peek-type">{hoverCard.typeLabel}</span>
            </div>
            {hoverCard.detail && <div className="editor-peek-detail">{hoverCard.detail}</div>}
            {hoverCard.chips.length > 0 && (
              <div className="editor-peek-chips">
                {hoverCard.chips.map((chip, i) => (
                  <span key={i} className="editor-peek-chip">
                    {chip}
                  </span>
                ))}
              </div>
            )}
            {hoverCard.relationships.length > 0 && (
              <div className="editor-peek-rels">
                {hoverCard.relationships.map((rel, i) => (
                  <div key={i} className="editor-peek-rel">
                    {rel}
                  </div>
                ))}
              </div>
            )}
            {hoverCard.sections.length > 0 && (
              <div className="editor-peek-chips">
                {hoverCard.sections.map((sec, i) => (
                  <span key={i} className="editor-peek-chip">
                    {sec}
                  </span>
                ))}
              </div>
            )}
            <button
              className="editor-peek-open"
              onClick={() => openHoveredEntity(hoverCard.entityType, hoverCard.entityId)}
            >
              {t('focusPeek.openEntity')}
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
