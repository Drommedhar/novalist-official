import { useTranslation } from 'react-i18next'
import { PanelLeft, PanelRight, Plus } from 'lucide-react'
import { useShellStore } from '../stores/shellStore'

export function Toolbar(): React.JSX.Element {
  const { t } = useTranslation()
  const toggleBinder = useShellStore((s) => s.toggleBinder)
  const toggleInspector = useShellStore((s) => s.toggleInspector)
  const isMac = window.novalist.platform === 'darwin'

  return (
    <header className={`toolbar${isMac ? ' toolbar-mac' : ''}`}>
      <button
        className="toolbar-button"
        title={t('shell.toggleBinder')}
        onClick={toggleBinder}
      >
        <PanelLeft size={16} strokeWidth={1.75} />
      </button>
      <div className="toolbar-book">Novalist</div>
      <button className="toolbar-button toolbar-action" disabled>
        <Plus size={14} strokeWidth={2} />
        {t('shell.newChapter')}
      </button>
      <button className="toolbar-button toolbar-action" disabled>
        <Plus size={14} strokeWidth={2} />
        {t('shell.newScene')}
      </button>
      <div className="toolbar-spacer" />
      <button
        className="toolbar-button"
        title={t('shell.toggleInspector')}
        onClick={toggleInspector}
      >
        <PanelRight size={16} strokeWidth={1.75} />
      </button>
    </header>
  )
}
