import type { CommandDef } from './commandTypes'
import { useEditorBridge } from '../stores/editorBridgeStore'
import { showDictation } from '../dictation/dictationStore'
import { shell, inEditor, editorOpen, hasSelection, sceneOpen, toggleEditorSetting } from './commandContext'

/** The named block styles a scene can carry. Body is the absence of a style. */
const PARAGRAPH_STYLES = ['', 'heading', 'subheading', 'blockquote', 'poetry'] as const

const PARAGRAPH_STYLE_COMMANDS: CommandDef[] = PARAGRAPH_STYLES.map((style) => ({
  id: `paragraph.style.${style || 'body'}`,
  labelKey: `blockStyle.${style || 'body'}`,
  categoryKey: 'hotkeys.category.editor',
  scope: 'paragraph' as const,
  available: editorOpen,
  run: inEditor((editor) => editor.setParagraphStyle(style))
}))

export const EDITOR_COMMANDS: CommandDef[] = [
  /* ── Selection: the floating toolbar over the prose ─────────────────── */
  {
    id: 'text.bold',
    labelKey: 'blockStyle.bold',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    // The gesture every writing program has had for thirty years. It was on
    // the binder instead, so on Windows and Linux there was no way to bold a
    // word from the keyboard at all - the binder has moved rather than bold.
    defaultGesture: 'Ctrl+B',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleBold())
  },
  {
    id: 'text.italic',
    labelKey: 'blockStyle.italic',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    // The editor forwards Ctrl+I and Ctrl+U to the host and suppresses the
    // native behaviour on the way, so with nothing bound here they did nothing
    // at all - worse than either half alone. (Cmd+I and Cmd+U stay native on
    // macOS, which editor.html deliberately protects.)
    defaultGesture: 'Ctrl+I',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleItalic())
  },
  {
    id: 'text.underline',
    labelKey: 'blockStyle.underline',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    defaultGesture: 'Ctrl+U',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleUnderline())
  },
  {
    id: 'text.strikethrough',
    labelKey: 'blockStyle.strikethrough',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleStrikethrough())
  },
  {
    id: 'text.highlight',
    labelKey: 'blockStyle.highlight',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleHighlight())
  },
  {
    id: 'text.link',
    labelKey: 'blockStyle.link',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    available: hasSelection,
    // The address is asked for by the frame around the editor, which is where
    // the prompt lives; the bridge only carries the answer back.
    run: () => useEditorBridge.getState().requestLink?.()
  },
  {
    id: 'text.comment',
    labelKey: 'blockStyle.comment',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    defaultGesture: 'Ctrl+Shift+M',
    available: hasSelection,
    run: inEditor((editor) => editor.addCommentToSelection(crypto.randomUUID()))
  },
  {
    id: 'text.footnote',
    labelKey: 'blockStyle.footnote',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    available: editorOpen,
    run: inEditor((editor) => editor.insertFootnoteAtSelection(crypto.randomUUID()))
  },
  {
    id: 'text.cutToDarlings',
    labelKey: 'editor.contextMenu.cutToDarlings',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    home: 'contextMenu',
    // Reads as one gesture with the passage it acts on, and the floating
    // toolbar is eight buttons of formatting - a command that removes prose
    // does not belong beside Bold.
    homeNote: 'Destructive, and read as part of the Scene group it sits in.',
    available: hasSelection,
    run: inEditor((editor) => editor.runContextAction('cutToDarlings'))
  },
  {
    id: 'text.auditionLine',
    labelKey: 'editor.contextMenu.auditionLine',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    home: 'contextMenu',
    // Read as one gesture with the line it acts on. The question it answers -
    // does this sound right in her mouth - is asked while looking at the line,
    // and a command that had to be found in a palette would be asked too late.
    homeNote: 'Acts on the line under the caret, and is read as part of the Scene group.', // aislop-ignore-line code-quality/duplicate-block -- Each editor command has its own ID, placement, availability, and action; repeated registry fields describe distinct commands.
    available: hasSelection,
    run: inEditor((editor) => editor.runContextAction('auditionLine'))
  },
  {
    id: 'text.createEntity',
    labelKey: 'editor.contextMenu.createEntity',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    home: 'contextMenu',
    homeNote: 'Belongs with the other Codex actions on the passage, not with formatting.',
    available: hasSelection, // aislop-ignore-line code-quality/duplicate-block -- Each editor command has its own ID, placement, availability, and action; repeated registry fields describe distinct commands.
    run: inEditor((editor) => editor.runContextAction('createEntityFromSelection'))
  },
  {
    id: 'text.appendToEntity',
    labelKey: 'editor.contextMenu.appendToEntity',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    home: 'contextMenu',
    homeNote: 'Belongs with the other Codex actions on the passage, not with formatting.',
    available: hasSelection,
    run: inEditor((editor) => editor.runContextAction('appendToEntitySection'))
  },

  /* ── Caret: the editor's context menu ───────────────────────────────── */
  {
    id: 'caret.peekEntity',
    labelKey: 'blockStyle.peekEntity',
    categoryKey: 'hotkeys.category.editor',
    scope: 'caret',
    defaultGesture: 'Ctrl+Shift+E',
    available: () => editorOpen() && useEditorBridge.getState().entityAtCaret,
    run: inEditor((editor) => editor.peekEntityAtCaret())
  },
  {
    id: 'caret.splitScene',
    labelKey: 'editor.contextMenu.splitScene',
    categoryKey: 'hotkeys.category.scenes',
    scope: 'caret',
    available: editorOpen,
    run: inEditor((editor) => editor.runContextAction('splitAtCaret'))
  },
  {
    id: 'caret.insertImage',
    labelKey: 'editor.contextMenu.insertImage',
    categoryKey: 'hotkeys.category.editor',
    scope: 'caret',
    available: editorOpen,
    run: inEditor((editor) => editor.runContextAction('insertImage'))
  },

  /* ── Paragraph: the writing view's command bar ──────────────────────── */
  ...PARAGRAPH_STYLE_COMMANDS,
  {
    id: 'paragraph.bulletList',
    labelKey: 'blockStyle.bulletList',
    categoryKey: 'hotkeys.category.editor',
    scope: 'paragraph',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleBulletList())
  },
  {
    id: 'paragraph.numberList',
    labelKey: 'blockStyle.numberList',
    categoryKey: 'hotkeys.category.editor',
    scope: 'paragraph',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleNumberList())
  },
  {
    id: 'paragraph.alignLeft',
    labelKey: 'blockStyle.left',
    categoryKey: 'hotkeys.category.editor',
    scope: 'paragraph',
    available: editorOpen,
    run: inEditor((editor) => editor.alignLeft())
  },
  {
    id: 'paragraph.alignCenter',
    labelKey: 'blockStyle.center',
    categoryKey: 'hotkeys.category.editor',
    scope: 'paragraph',
    available: editorOpen,
    run: inEditor((editor) => editor.alignCenter())
  },
  {
    id: 'paragraph.alignRight',
    labelKey: 'blockStyle.right',
    categoryKey: 'hotkeys.category.editor',
    scope: 'paragraph',
    available: editorOpen,
    run: inEditor((editor) => editor.alignRight())
  },
  {
    id: 'paragraph.alignJustify',
    labelKey: 'blockStyle.justify',
    categoryKey: 'hotkeys.category.editor',
    scope: 'paragraph',
    available: editorOpen,
    run: inEditor((editor) => editor.alignJustify())
  },

  /* ── The writing view itself ────────────────────────────────────────── */
  {
    id: 'write.dictate',
    labelKey: 'dictation.title',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: () => { void showDictation() }
  },
  {
    id: 'write.snapshots',
    labelKey: 'shell.snapshots',
    categoryKey: 'hotkeys.category.scenes',
    scope: 'view',
    available: sceneOpen,
    run: () => shell().openDialog('snapshots')
  },
  {
    id: 'write.suggestionMode',
    labelKey: 'suggestions.mode',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: () => shell().toggleSuggestionMode()
  },
  {
    id: 'write.readAloud',
    labelKey: 'blockStyle.readAloud',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: () => useEditorBridge.getState().toggleReadAloud?.()
  },
  {
    id: 'write.readability',
    labelKey: 'blockStyle.readability',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: toggleEditorSetting('readabilityHighlighting')
  },
  {
    id: 'write.composeDimming',
    labelKey: 'blockStyle.composeDimming',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: toggleEditorSetting('composeDimming')
  },
  {
    id: 'write.typewriterScrolling', // aislop-ignore-line code-quality/duplicate-block -- Each editor command has its own ID, placement, availability, and action; repeated registry fields describe distinct commands.
    labelKey: 'blockStyle.typewriterScrolling',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: toggleEditorSetting('typewriterScrollEnabled')
  },
  {
    id: 'write.pageView',
    labelKey: 'blockStyle.pageView',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: toggleEditorSetting('pageViewEnabled')
  }
]
