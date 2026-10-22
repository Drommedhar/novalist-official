import { rpc } from '../rpc/client'
import { useHostedSessionStore } from './hostedSessionStore'
import { reconcileSceneEditors, useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { useCodexStore } from '../stores/codexStore'
import { useHostBridgeStore } from '../stores/hostBridgeStore'

interface ProjectUpdate { sessionId: string; state: ProjectStateDto }

export function registerSharedProjectNotifications(): void {
  rpc.onNotification('collaboration/projectUpdated', params => {
    const change = (Array.isArray(params) ? params[0] : params) as ProjectUpdate | undefined
    if (!change || useHostedSessionStore.getState().session?.sessionId !== change.sessionId) return
    const project = useProjectStore.getState()
    if (project.projectPath !== change.state.projectPath || project.workspaceBusy) return
    project.applyState(change.state)
    useProjectStore.setState(state => reconcileSceneEditors(state, change.state.chapters))
    void refreshCodex().catch(error => useHostBridgeStore.getState().pushToast(String(error)))
  })
}

async function refreshCodex(): Promise<void> {
  const codex = useCodexStore.getState()
  const id = codex.selectedId
  const type = codex.entityType
  await codex.refresh()
  if (useCodexStore.getState().selectedId !== id || useCodexStore.getState().entityType !== type || !id) return
  if (useCodexStore.getState().entities.some(entity => entity.id === id)) await useCodexStore.getState().select(id)
  else useCodexStore.setState({ selectedId: null, selectedRecord: null })
}
