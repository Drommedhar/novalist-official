import {
  AlignCenter,
  AlignJustify,
  AlignLeft,
  AlignRight,
  Bold,
  Eye,
  Gauge,
  Highlighter,
  Italic,
  Link,
  List,
  ListOrdered,
  MessageSquare,
  NotebookText,
  PenLine,
  Settings2,
  Square,
  Strikethrough,
  Underline,
  Volume2
} from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSettingsStore } from '../../stores/settingsStore'
import { useShellStore } from '../../stores/shellStore'
import type { EditorWindow } from './editorBridge'

export interface FormattingState {
  bold: boolean
  italic: boolean
  underline: boolean
  strikethrough: boolean
  highlight: boolean
  alignment: 'left' | 'center' | 'right' | 'justify'
  /** Named block style on the paragraph the caret is in; empty for body text. */
  paragraphStyle: string
  bulletList: boolean
  numberList: boolean
  /** True only for a non-empty text selection. */
  hasSelection: boolean
  linkActive: boolean
  entityAtCaret: boolean
}

interface EditorToolbarProps {
  formatting: FormattingState
  editor(): EditorWindow | null
  /** True while the scene is being read back, so the button offers Stop. */
  speaking: boolean
  onToggleReadAloud(): void
  onRequestLink(): void
  onAddComment(): void
  onAddFootnote(): void
}

/**
 * The named block styles a scene can carry. Body is the absence of a style
 * rather than a style of its own, which is what the editor stores and what keeps
 * an untouched manuscript free of markup it never asked for.
 */
const PARAGRAPH_STYLES = ['', 'heading', 'subheading', 'blockquote', 'poetry'] as const

/** Formatting strip above the editor; commands run inside editor.html. */
export function EditorToolbar({
  formatting,
  editor,
  speaking,
  onToggleReadAloud,
  onRequestLink,
  onAddComment,
  onAddFootnote
}: EditorToolbarProps): React.JSX.Element {
  const { t } = useTranslation()
  const run = (command: (e: EditorWindow) => void): void => {
    const live = editor()
    if (live) command(live)
  }

  const buttons: {
    key: string
    context: 'selection' | 'caret'
    active: boolean
    icon: React.ComponentType<{ size?: number; strokeWidth?: number }>
    run(e: EditorWindow): void
  }[] = [
    { key: 'bold', context: 'selection', active: formatting.bold, icon: Bold, run: (e) => e.toggleBold() },
    { key: 'italic', context: 'selection', active: formatting.italic, icon: Italic, run: (e) => e.toggleItalic() },
    { key: 'underline', context: 'selection', active: formatting.underline, icon: Underline, run: (e) => e.toggleUnderline() },
    { key: 'strikethrough', context: 'selection', active: formatting.strikethrough, icon: Strikethrough, run: (e) => e.toggleStrikethrough() },
    { key: 'highlight', context: 'selection', active: formatting.highlight, icon: Highlighter, run: (e) => e.toggleHighlight() },
    { key: 'bulletList', context: 'caret', active: formatting.bulletList, icon: List, run: (e) => e.toggleBulletList() },
    {
      key: 'numberList',
      context: 'caret',
      active: formatting.numberList,
      icon: ListOrdered,
      run: (e) => e.toggleNumberList()
    },
    { key: 'left', context: 'caret', active: formatting.alignment === 'left', icon: AlignLeft, run: (e) => e.alignLeft() },
    { key: 'center', context: 'caret', active: formatting.alignment === 'center', icon: AlignCenter, run: (e) => e.alignCenter() },
    { key: 'right', context: 'caret', active: formatting.alignment === 'right', icon: AlignRight, run: (e) => e.alignRight() },
    { key: 'justify', context: 'caret', active: formatting.alignment === 'justify', icon: AlignJustify, run: (e) => e.alignJustify() }
  ]

  const [suggesting, setSuggesting] = useState(false)
  const [optionsOpen, setOptionsOpen] = useState(false)
  // Whose suggestion it is. The author's own name is the only one Novalist
  // knows; an editor working in someone else's copy sets it in Settings.
  const author = useSettingsStore((s) => s.view?.effective.reviewerName ?? '')

  const settingsView = useSettingsStore((s) => s.view)
  const effective = settingsView?.effective
  const editorScope = settingsView?.overriddenSections?.editor ? 'project' : 'global'
  const updateEditorSetting = (patch: Record<string, unknown>): void => {
    void useSettingsStore.getState().update(editorScope, patch)
  }

  return (
    <div
      className="editor-toolbar"
      data-editor-context={formatting.hasSelection ? 'selection' : 'caret'}
    >
      <div className="editor-toolbar-primary" role="toolbar" aria-label={t('blockStyle.toolbar')}>
        {!formatting.hasSelection && (
          <select
            className="editor-toolbar-style"
            value={formatting.paragraphStyle}
            title={t('blockStyle.label')}
            aria-label={t('blockStyle.label')}
            onChange={(event) => run((live) => live.setParagraphStyle(event.target.value))}
          >
            {PARAGRAPH_STYLES.map((style) => (
              <option key={style || 'body'} value={style}>
                {t(`blockStyle.${style || 'body'}`)}
              </option>
            ))}
          </select>
        )}
        {buttons
          .filter(({ context }) =>
            formatting.hasSelection ? context === 'selection' : context === 'caret'
          )
          .map(({ key, active, icon: Icon, run: command }) => (
            <button
              type="button"
              key={key}
              className={`editor-toolbar-button${active ? ' active' : ''}`}
              title={t(`blockStyle.${key}`)}
              aria-label={t(`blockStyle.${key}`)}
              aria-pressed={active}
              onClick={() => run(command)}
            >
              <Icon size={15} strokeWidth={1.75} />
            </button>
          ))}

        {formatting.hasSelection && (
          <>
            <button
              type="button"
              className={`editor-toolbar-button${formatting.linkActive ? ' active' : ''}`}
              title={t('blockStyle.link')}
              aria-label={t('blockStyle.link')}
              aria-pressed={formatting.linkActive}
              onClick={onRequestLink}
            >
              <Link size={15} strokeWidth={1.75} />
            </button>
            <button
              type="button"
              className="editor-toolbar-button editor-toolbar-labelled"
              title={t('editor.contextMenu.addComment')}
              onClick={onAddComment}
            >
              <MessageSquare size={15} strokeWidth={1.75} />
              <span>{t('blockStyle.comment')}</span>
            </button>
            <button
              type="button"
              className="editor-toolbar-button editor-toolbar-labelled"
              title={t('editor.contextMenu.addFootnote')}
              onClick={onAddFootnote}
            >
              <NotebookText size={15} strokeWidth={1.75} />
              <span>{t('blockStyle.footnote')}</span>
            </button>
          </>
        )}

        {!formatting.hasSelection && formatting.entityAtCaret && (
          <button
            type="button"
            className="editor-toolbar-button editor-toolbar-labelled"
            title={t('blockStyle.peekEntity')}
            onClick={() => run((live) => live.peekEntityAtCaret())}
          >
            <Eye size={15} strokeWidth={1.75} />
            <span>{t('blockStyle.peekEntity')}</span>
          </button>
        )}

        <span className="toolbar-spacer" />
        <button
          type="button"
          className={`editor-toolbar-button${suggesting ? ' active' : ''}`}
          title={t('suggestions.mode')}
          aria-pressed={suggesting}
          onClick={() => {
            const next = !suggesting
            setSuggesting(next)
            run((live) => live.setSuggestionMode(next, author))
          }}
        >
          <PenLine size={15} strokeWidth={1.75} />
        </button>
        <button
          type="button"
          className={`editor-toolbar-button${optionsOpen ? ' active' : ''}`}
          title={t('blockStyle.writingOptions')}
          aria-label={t('blockStyle.writingOptions')}
          aria-expanded={optionsOpen}
          onClick={() => setOptionsOpen((open) => !open)}
        >
          <Settings2 size={15} strokeWidth={1.75} />
        </button>
      </div>

      {(speaking || suggesting) && (
        <div className="editor-mode-bar" role="status">
          <span>{t(speaking ? 'blockStyle.readAloudActive' : 'blockStyle.suggestionActive')}</span>
          <button
            type="button"
            onClick={() => {
              if (speaking) onToggleReadAloud()
              if (suggesting) {
                setSuggesting(false)
                run((live) => live.setSuggestionMode(false, author))
              }
            }}
          >
            <Square size={14} strokeWidth={1.75} />
            {t('blockStyle.exitMode')}
          </button>
        </div>
      )}

      {optionsOpen && (
        <div
          className="editor-writing-options"
          role="dialog"
          aria-label={t('blockStyle.writingOptions')}
        >
          <label>
            <input
              type="checkbox"
              checked={effective?.readabilityHighlighting ?? false}
              onChange={(event) =>
                updateEditorSetting({ readabilityHighlighting: event.target.checked })
              }
            />
            <Gauge size={15} strokeWidth={1.75} />
            {t('blockStyle.readability')}
          </label>
          <label>
            <input
              type="checkbox"
              checked={effective?.composeDimming ?? false}
              onChange={(event) => updateEditorSetting({ composeDimming: event.target.checked })}
            />
            {t('blockStyle.composeDimming')}
          </label>
          <label>
            <input
              type="checkbox"
              checked={effective?.typewriterScrollEnabled ?? false}
              onChange={(event) =>
                updateEditorSetting({ typewriterScrollEnabled: event.target.checked })
              }
            />
            {t('blockStyle.typewriterScrolling')}
          </label>
          <label>
            <input
              type="checkbox"
              checked={effective?.pageViewEnabled ?? false}
              onChange={(event) => updateEditorSetting({ pageViewEnabled: event.target.checked })}
            />
            {t('blockStyle.pageView')}
          </label>
          <button type="button" onClick={() => useShellStore.getState().toggleFocusMode()}>
            {t(useShellStore.getState().focusMode ? 'blockStyle.leaveFocus' : 'blockStyle.enterFocus')}
          </button>
          <button type="button" onClick={onToggleReadAloud}>
            {speaking ? (
              <Square size={15} strokeWidth={1.75} />
            ) : (
              <Volume2 size={15} strokeWidth={1.75} />
            )}
            {t(speaking ? 'blockStyle.readAloudStop' : 'blockStyle.readAloud')}
          </button>
          <button type="button" onClick={() => useShellStore.getState().openSettings('editor')}>
            {t('blockStyle.moreWritingSettings')}
          </button>
        </div>
      )}
    </div>
  )
}
