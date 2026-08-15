import {
  AlignCenter,
  AlignJustify,
  AlignLeft,
  AlignRight,
  Gauge,
  History,
  List,
  ListOrdered,
  PenLine,
  Settings2,
  Square,
  Volume2
} from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { runCommand } from '../../shell/commands'
import { useSettingsStore } from '../../stores/settingsStore'
import { useShellStore } from '../../stores/shellStore'

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
  /** True while the scene is being read back, so the bar offers Stop. */
  speaking: boolean
}

/**
 * The writing view's own command bar.
 *
 * It used to hold the inline marks, the link, Comment and Footnote as well -
 * every one of them a second copy of a button already in the floating toolbar
 * that appears over a selection, and Comment and Footnote a third copy of one
 * already in the context menu. It also swapped its whole contents depending on
 * whether anything was selected, so the row of buttons a writer had just
 * learned moved out from under them the moment they dragged across a word.
 *
 * Under the placement law it keeps what acts on the paragraph the caret is in
 * and on the open scene - structure, not inline formatting - and that set does
 * not change as the selection does. Anything acting on selected text lives in
 * the floating toolbar over the selection, and nowhere else.
 */
// placement-container: viewBar
const PARAGRAPH_STYLES = ['', 'heading', 'subheading', 'blockquote', 'poetry'] as const

export function EditorToolbar({ formatting, speaking }: EditorToolbarProps): React.JSX.Element {
  const { t } = useTranslation()
  const [optionsOpen, setOptionsOpen] = useState(false)
  const suggesting = useShellStore((s) => s.suggestionMode)
  const effective = useSettingsStore((s) => s.view?.effective)

  const buttons: {
    command: string
    labelKey: string
    active: boolean
    icon: React.ComponentType<{ size?: number; strokeWidth?: number }>
  }[] = [
    {
      command: 'paragraph.bulletList',
      labelKey: 'blockStyle.bulletList',
      active: formatting.bulletList,
      icon: List
    },
    {
      command: 'paragraph.numberList',
      labelKey: 'blockStyle.numberList',
      active: formatting.numberList,
      icon: ListOrdered
    },
    {
      command: 'paragraph.alignLeft',
      labelKey: 'blockStyle.left',
      active: formatting.alignment === 'left',
      icon: AlignLeft
    },
    {
      command: 'paragraph.alignCenter',
      labelKey: 'blockStyle.center',
      active: formatting.alignment === 'center',
      icon: AlignCenter
    },
    {
      command: 'paragraph.alignRight',
      labelKey: 'blockStyle.right',
      active: formatting.alignment === 'right',
      icon: AlignRight
    },
    {
      command: 'paragraph.alignJustify',
      labelKey: 'blockStyle.justify',
      active: formatting.alignment === 'justify',
      icon: AlignJustify
    }
  ]

  /** One writing preference, flipped where the writer is already editing. */
  const option = (option: {
    setting: string
    command: string
    labelKey: string
    icon?: React.JSX.Element
  }): React.JSX.Element => (
    <label key={option.setting} data-command={option.command}>
      <input
        type="checkbox"
        checked={(effective as Record<string, unknown> | undefined)?.[option.setting] === true}
        onChange={() => runCommand(option.command)}
      />
      {option.icon}
      {t(option.labelKey)}
    </label>
  )

  return (
    <div className="editor-toolbar">
      <div className="editor-toolbar-primary" role="toolbar" aria-label={t('blockStyle.toolbar')}>
        <select
          className="editor-toolbar-style"
          data-command="paragraph.style.body paragraph.style.heading paragraph.style.subheading paragraph.style.blockquote paragraph.style.poetry"
          value={formatting.paragraphStyle}
          title={t('blockStyle.label')}
          aria-label={t('blockStyle.label')}
          onChange={(event) =>
            runCommand(`paragraph.style.${event.target.value || 'body'}`)
          }
        >
          {PARAGRAPH_STYLES.map((style) => (
            <option key={style || 'body'} value={style}>
              {t(`blockStyle.${style || 'body'}`)}
            </option>
          ))}
        </select>
        {buttons.map(({ command, labelKey, active, icon: Icon }) => (
          <button
            type="button"
            key={command}
            data-command={command}
            className={`editor-toolbar-button${active ? ' active' : ''}`}
            title={t(labelKey)}
            aria-label={t(labelKey)}
            aria-pressed={active}
            onClick={() => runCommand(command)}
          >
            <Icon size={15} strokeWidth={1.75} />
          </button>
        ))}

        <span className="toolbar-spacer" />
        {/* The scene's own history. It was a button on the main toolbar, which
            is the project's bar - a snapshot is of the scene in front of you. */}
        <button
          type="button"
          data-command="write.snapshots"
          className="editor-toolbar-button"
          title={t('shell.snapshots')}
          aria-label={t('shell.snapshots')}
          onClick={() => runCommand('write.snapshots')}
        >
          <History size={15} strokeWidth={1.75} />
        </button>
        <button
          type="button"
          data-command="write.suggestionMode"
          className={`editor-toolbar-button${suggesting ? ' active' : ''}`}
          title={t('suggestions.mode')}
          aria-label={t('suggestions.mode')}
          aria-pressed={suggesting}
          onClick={() => runCommand('write.suggestionMode')}
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
              if (speaking) runCommand('write.readAloud')
              if (suggesting) runCommand('write.suggestionMode')
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
          {option({
            setting: 'readabilityHighlighting',
            command: 'write.readability',
            labelKey: 'blockStyle.readability',
            icon: <Gauge size={15} strokeWidth={1.75} />
          })}
          {option({
            setting: 'composeDimming',
            command: 'write.composeDimming',
            labelKey: 'blockStyle.composeDimming'
          })}
          {option({
            setting: 'typewriterScrollEnabled',
            command: 'write.typewriterScrolling',
            labelKey: 'blockStyle.typewriterScrolling'
          })}
          {option({
            setting: 'pageViewEnabled',
            command: 'write.pageView',
            labelKey: 'blockStyle.pageView'
          })}
          <button
            type="button"
            data-command="write.readAloud"
            onClick={() => runCommand('write.readAloud')}
          >
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
      {/* placement-container: end */}
    </div>
  )
}
