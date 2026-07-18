import { useTranslation } from 'react-i18next'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { EditorFrame } from '../views/editor/EditorFrame'
import { CodexView } from '../views/codex/CodexView'
import { DashboardView } from '../views/dashboard/DashboardView'
import { ManuscriptView } from '../views/manuscript/ManuscriptView'

export function MainArea(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const openSceneId = useProjectStore((s) => s.openSceneId)

  if (mainView === 'write') {
    return (
      <main className="main-area">
        {openSceneId ? (
          <EditorFrame />
        ) : (
          <div className="main-placeholder">
            <h1>{t('shell.view.write')}</h1>
            <p>{t('shell.binderEmpty')}</p>
          </div>
        )}
      </main>
    )
  }

  if (mainView === 'codex') {
    return (
      <main className="main-area">
        <CodexView />
      </main>
    )
  }

  if (mainView === 'dashboard') {
    return (
      <main className="main-area">
        <DashboardView />
      </main>
    )
  }

  if (mainView === 'manuscript') {
    return (
      <main className="main-area">
        <ManuscriptView />
      </main>
    )
  }

  return (
    <main className="main-area">
      <div className="main-placeholder">
        <h1>{t(`shell.view.${mainView}`)}</h1>
        <p>{t('shell.viewPending')}</p>
      </div>
    </main>
  )
}
