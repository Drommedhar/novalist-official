import { useProjectStore } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'

export async function handleMobileBack(): Promise<void> {
  const project = useProjectStore.getState()
  const shell = useShellStore.getState()
  if (!project.isLoaded) return
  if (shell.mobileLayout === 'tablet') {
    if (shell.mainView !== 'dashboard') {
      shell.setMainView('dashboard')
      return
    }
  } else {
    if (shell.mobileTab === 'manuscript' && project.openSceneId && project.activeEditorPaneId) {
      await project.closeTab(project.activeEditorPaneId, project.openSceneId)
      return
    }
    if (shell.mobileTab !== 'dashboard') {
      shell.setMobileTab('dashboard')
      return
    }
  }
  await project.closeProject()
}
