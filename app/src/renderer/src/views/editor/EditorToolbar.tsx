import {
  AlignCenter,
  AlignJustify,
  AlignLeft,
  AlignRight,
  Bold,
  BookOpen,
  Gauge,
  Italic,
  List,
  ListOrdered,
  Square,
  Underline,
  Volume2
} from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useSettingsStore } from '../../stores/settingsStore'
import type { EditorWindow } from './editorBridge'

export interface FormattingState {
  bold: boolean
  italic: boolean
  underline: boolean
  alignment: 'left' | 'center' | 'right' | 'justify'
  /** Named block style on the paragraph the caret is in; empty for body text. */
  paragraphStyle: string
  bulletList: boolean
  numberList: boolean
}

interface EditorToolbarProps {
  formatting: FormattingState
  editor(): EditorWindow | null
  /** True while the scene is being read back, so the button offers Stop. */
  speaking: boolean
  onToggleReadAloud(): void
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
  onToggleReadAloud
}: EditorToolbarProps): React.JSX.Element {
  const { t } = useTranslation()
  const run = (command: (e: EditorWindow) => void): void => {
    const live = editor()
    if (live) command(live)
  }

  const buttons: {
    key: string
    active: boolean
    icon: React.ComponentType<{ size?: number; strokeWidth?: number }>
    run(e: EditorWindow): void
  }[] = [
    { key: 'bold', active: formatting.bold, icon: Bold, run: (e) => e.toggleBold() },
    { key: 'italic', active: formatting.italic, icon: Italic, run: (e) => e.toggleItalic() },
    { key: 'underline', active: formatting.underline, icon: Underline, run: (e) => e.toggleUnderline() },
    { key: 'bulletList', active: formatting.bulletList, icon: List, run: (e) => e.toggleBulletList() },
    {
      key: 'numberList',
      active: formatting.numberList,
      icon: ListOrdered,
      run: (e) => e.toggleNumberList()
    },
    { key: 'left', active: formatting.alignment === 'left', icon: AlignLeft, run: (e) => e.alignLeft() },
    { key: 'center', active: formatting.alignment === 'center', icon: AlignCenter, run: (e) => e.alignCenter() },
    { key: 'right', active: formatting.alignment === 'right', icon: AlignRight, run: (e) => e.alignRight() },
    { key: 'justify', active: formatting.alignment === 'justify', icon: AlignJustify, run: (e) => e.alignJustify() }
  ]

  const pageView = useSettingsStore((s) => s.view?.effective.pageViewEnabled ?? false)
  const readability = useSettingsStore(
    (s) => s.view?.effective.readabilityHighlighting ?? false
  )

  return (
    <div className="editor-toolbar">
      <select
        className="editor-toolbar-style"
        value={formatting.paragraphStyle}
        title={t('blockStyle.label')}
        onChange={(e) => run((live) => live.setParagraphStyle(e.target.value))}
      >
        {PARAGRAPH_STYLES.map((style) => (
          <option key={style || 'body'} value={style}>
            {t(`blockStyle.${style || 'body'}`)}
          </option>
        ))}
      </select>
      {buttons.map(({ key, active, icon: Icon, run: cmd }) => (
        <button
          key={key}
          className={`editor-toolbar-button${active ? ' active' : ''}`}
          title={t(`blockStyle.${key}`)}
          onClick={() => run(cmd)}
        >
          <Icon size={15} strokeWidth={1.75} />
        </button>
      ))}
      <span className="toolbar-spacer" />
      <button
        className={`editor-toolbar-button${readability ? ' active' : ''}`}
        title={t('blockStyle.readability')}
        onClick={() =>
          void useSettingsStore
            .getState()
            .update('global', { readabilityHighlighting: !readability })
        }
      >
        <Gauge size={15} strokeWidth={1.75} />
      </button>
      <button
        className={`editor-toolbar-button${speaking ? ' active' : ''}`}
        title={t(speaking ? 'blockStyle.readAloudStop' : 'blockStyle.readAloud')}
        onClick={onToggleReadAloud}
      >
        {speaking ? <Square size={15} strokeWidth={1.75} /> : <Volume2 size={15} strokeWidth={1.75} />}
      </button>
      <button
        className={`editor-toolbar-button${pageView ? ' active' : ''}`}
        title={t('blockStyle.pageView')}
        onClick={() => void useSettingsStore.getState().update('global', { pageViewEnabled: !pageView })}
      >
        <BookOpen size={15} strokeWidth={1.75} />
      </button>
    </div>
  )
}
