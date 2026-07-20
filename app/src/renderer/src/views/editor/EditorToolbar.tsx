import { AlignCenter, AlignJustify, AlignLeft, AlignRight, Bold, BookOpen, Italic, Underline } from 'lucide-react'
import { useSettingsStore } from '../../stores/settingsStore'
import type { EditorWindow } from './editorBridge'

export interface FormattingState {
  bold: boolean
  italic: boolean
  underline: boolean
  alignment: 'left' | 'center' | 'right' | 'justify'
}

interface EditorToolbarProps {
  formatting: FormattingState
  editor(): EditorWindow | null
}

/** Formatting strip above the editor; commands run inside editor.html. */
export function EditorToolbar({ formatting, editor }: EditorToolbarProps): React.JSX.Element {
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
    { key: 'left', active: formatting.alignment === 'left', icon: AlignLeft, run: (e) => e.alignLeft() },
    { key: 'center', active: formatting.alignment === 'center', icon: AlignCenter, run: (e) => e.alignCenter() },
    { key: 'right', active: formatting.alignment === 'right', icon: AlignRight, run: (e) => e.alignRight() },
    { key: 'justify', active: formatting.alignment === 'justify', icon: AlignJustify, run: (e) => e.alignJustify() }
  ]

  const pageView = useSettingsStore((s) => s.view?.effective.pageViewEnabled ?? false)

  return (
    <div className="editor-toolbar">
      {buttons.map(({ key, active, icon: Icon, run: cmd }) => (
        <button
          key={key}
          className={`editor-toolbar-button${active ? ' active' : ''}`}
          onClick={() => run(cmd)}
        >
          <Icon size={15} strokeWidth={1.75} />
        </button>
      ))}
      <span className="toolbar-spacer" />
      <button
        className={`editor-toolbar-button${pageView ? ' active' : ''}`}
        onClick={() => void useSettingsStore.getState().update('global', { pageViewEnabled: !pageView })}
      >
        <BookOpen size={15} strokeWidth={1.75} />
      </button>
    </div>
  )
}
