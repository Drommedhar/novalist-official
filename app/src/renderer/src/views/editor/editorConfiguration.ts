import type { TFunction } from 'i18next'
import { inlineActionDescriptorsJson, extensionContextMenuItemsJson, type EditorWindow } from './editorBridge'
import { useSettingsStore } from '../../stores/settingsStore'
import { rpc } from '../../rpc/client'

/**
 * The two hard bands of the readability ramp, as a wash rather than a fill -
 * the words still have to be readable through it. Read off the design tokens
 * because the editor is a separate document with no access to them, which also
 * means a theme that restates the ramp is honoured for free.
 */
export function hardBandColors(): Record<string, string> {
  const style = getComputedStyle(document.documentElement)
  const wash = (token: string, percent: number): string =>
    `color-mix(in srgb, ${style.getPropertyValue(token).trim()} ${percent}%, transparent)`
  return {
    Difficult: wash('--nl-readability-difficult', 22),
    VeryDifficult: wash('--nl-readability-very-difficult', 30)
  }
}

export function pushEditorSettings(editor: EditorWindow, initial = false): void {
  const view = useSettingsStore.getState().view
  if (!view) return
  const eff = view.effective
  editor.setFont(eff.editorFontFamily, eff.editorFontSize)
  editor.setReadingComfort(
    eff.editorLineHeight,
    eff.editorLetterSpacing,
    eff.editorParagraphSpacing,
    eff.editorFirstLineIndent
  )
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
  'sagte',
  'fragte',
  'rief',
  'schrie',
  'flüsterte',
  'erwiderte',
  'antwortete',
  'murmelte',
  'brummte',
  'zischte',
  'seufzte',
  'stöhnte',
  'meinte',
  'entgegnete',
  'sprach',
  'erklärte',
  'bemerkte',
  'bat',
  'flehte',
  'knurrte',
  'hauchte',
  'jammerte',
  'klagte',
  'stotterte',
  'stammelte',
  'schluchzte',
  'keuchte',
  'wimmerte',
  'drängte',
  'forderte',
  'befahl',
  'warnte',
  'mahnte',
  'tröstete',
  'beruhigte'
]

const DIALOGUE_VERBS_EN = [
  'said',
  'asked',
  'whispered',
  'shouted',
  'cried',
  'replied',
  'answered',
  'murmured',
  'exclaimed',
  'muttered',
  'yelled',
  'screamed',
  'called',
  'remarked',
  'responded',
  'explained',
  'stated',
  'declared',
  'added',
  'continued',
  'insisted',
  'suggested',
  'wondered',
  'demanded',
  'pleaded',
  'begged',
  'stammered',
  'stuttered',
  'sobbed',
  'groaned',
  'sighed',
  'breathed',
  'hissed',
  'snapped',
  'barked',
  'growled',
  'urged',
  'warned',
  'cautioned',
  'consoled'
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
export function pushEditorConfig(editor: EditorWindow, t: TFunction): void {
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
      bold: t('blockStyle.bold'),
      italic: t('blockStyle.italic'),
      underline: t('blockStyle.underline'),
      strikethrough: t('blockStyle.strikethrough'),
      highlight: t('blockStyle.highlight'),
      link: t('blockStyle.link'),
      peekEntity: t('blockStyle.peekEntity'),
      addToDictionary: t('editor.contextMenu.addToDictionary'),
      removeText: t('dialog.delete'),
      createEntity: t('editor.contextMenu.createEntity'),
      appendToEntity: t('editor.contextMenu.appendToEntity'),
      splitScene: t('editor.contextMenu.splitScene'),
      insertImage: t('editor.contextMenu.insertImage'),
      cutToDarlings: t('editor.contextMenu.cutToDarlings'),
      auditionLine: t('editor.contextMenu.auditionLine'),
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
