import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { PanelLeft, PanelRight, Plus, Search } from 'lucide-react'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { InputDialog } from './InputDialog'
import { FindReplaceDialog } from './FindReplaceDialog'

type PendingDialog = 'chapter' | 'scene' | 'find' | null

export function Toolbar(): React.JSX.Element {
  const { t } = useTranslation()
  const toggleBinder = useShellStore((s) => s.toggleBinder)
  const toggleInspector = useShellStore((s) => s.toggleInspector)
  const projectName = useProjectStore((s) => s.projectName)
  const chapters = useProjectStore((s) => s.chapters)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const createChapter = useProjectStore((s) => s.createChapter)
  const createScene = useProjectStore((s) => s.createScene)
  const [dialog, setDialog] = useState<PendingDialog>(null)
  const isMac = window.novalist.platform === 'darwin'

  const targetChapter = openChapterGuid ?? chapters[chapters.length - 1]?.guid ?? null

  return (
    <header className={`toolbar${isMac ? ' toolbar-mac' : ''}`}>
      <button className="toolbar-button" title={t('shell.toggleBinder')} onClick={toggleBinder}>
        <PanelLeft size={16} strokeWidth={1.75} />
      </button>
      <div className="toolbar-book">{projectName ?? 'Novalist'}</div>
      <button className="toolbar-button toolbar-action" onClick={() => setDialog('chapter')}>
        <Plus size={14} strokeWidth={2} />
        {t('shell.newChapter')}
      </button>
      <button
        className="toolbar-button toolbar-action"
        disabled={targetChapter === null}
        onClick={() => setDialog('scene')}
      >
        <Plus size={14} strokeWidth={2} />
        {t('shell.newScene')}
      </button>
      <div className="toolbar-spacer" />
      <button
        className="toolbar-button"
        title={t('findReplace.title')}
        onClick={() => setDialog('find')}
      >
        <Search size={15} strokeWidth={1.75} />
      </button>
      <button
        className="toolbar-button"
        title={t('shell.toggleInspector')}
        onClick={toggleInspector}
      >
        <PanelRight size={16} strokeWidth={1.75} />
      </button>
      {dialog === 'chapter' && (
        <InputDialog
          title={t('dialog.chapterName')}
          placeholder={t('dialog.chapterNameWatermark')}
          onCancel={() => setDialog(null)}
          onSubmit={(title) => {
            setDialog(null)
            void createChapter(title)
          }}
        />
      )}
      {dialog === 'find' && <FindReplaceDialog onClose={() => setDialog(null)} />}
      {dialog === 'scene' && targetChapter !== null && (
        <InputDialog
          title={t('shell.newScene')}
          onCancel={() => setDialog(null)}
          onSubmit={(title) => {
            setDialog(null)
            void createScene(targetChapter, title)
          }}
        />
      )}
    </header>
  )
}
