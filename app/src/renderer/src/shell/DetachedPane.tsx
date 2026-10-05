import { useDetachedCommands } from './useDetachedCommands'
import { ShellDialogs } from './ShellDialogs'
import { WorkspaceRecoveryDialog } from './WorkspaceRecoveryDialog'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { MainArea } from './MainArea'
import { StatusBar } from './StatusBar'
import { useProjectStore } from '../stores/projectStore'
import { newLeaf, useShellStore, type MainView } from '../stores/shellStore'
import { useUiScaleStore } from '../stores/uiScaleStore'
import { hydrateWindow } from './bootstrap'
import { useWorkspaceWindow } from './useWorkspaceWindow'
import { SceneConflictDialog } from './SceneConflictDialog'
import { UnsavedLeaveDialog } from './UnsavedLeaveDialog'
import { useHostBridgeStore } from '../stores/hostBridgeStore'

/** What the window was torn off to show. */
export interface DetachedRequest {
  view: MainView
  /** The project the window it came from has open. */
  projectPath: string | null
  /** The scene it was showing, when the editor is what was torn off. */
  chapterGuid: string | null
  sceneId: string | null
}

/**
 * A window holding one torn-off pane.
 *
 * The Codex on a second monitor while the manuscript stays where it is. It runs
 * the same renderer and gets its own channel to the same backend, so the view
 * inside is the real one rather than a picture of it - edits made here land in
 * the project like any other.
 *
 * No binder and no activity bar: a torn-off pane is one thing on purpose, and
 * navigation belongs to the window the writer is working in. The pane's own
 * header carries the view picker, so the window can still be pointed at
 * something else - and can be split, which is how a second scene gets in here.
 */
export function DetachedPane({ request }: { request: DetachedRequest }): React.JSX.Element {
  const { t } = useTranslation()
  useWorkspaceWindow()
  useDetachedCommands()
  const workspaceBusy = useProjectStore((state) => state.workspaceBusy)
  const [ready, setReady] = useState(false)

  useEffect(() => {
    useUiScaleStore.getState().apply()
    const reportWidth = (): void => useShellStore.getState().setShellMetrics(window.innerWidth)
    reportWidth()
    window.addEventListener('resize', reportWidth)
    const leaf = newLeaf(request.view)
    useShellStore.setState({ panes: leaf, activePaneId: leaf.id, mainView: request.view })
    let disposed = false
    const stopReconnect = rpc.onReconnected(() => void hydrateWindow().catch(() => {}))
    void rpc.connect().then(hydrateWindow).then(async () => {
      if (disposed) return
      const project = useProjectStore.getState()
      // A detached view joins the current shared workspace; opening a second
      // project here would replace the workspace behind every other window.
      const chapter = project.chapters.find((item) => item.guid === request.chapterGuid)
      if (request.sceneId && chapter?.scenes.some((scene) => scene.id === request.sceneId)) {
        await project.openSceneIn(leaf.id, chapter.guid, request.sceneId)
      }
    }).catch((error) => {
      useHostBridgeStore.getState().pushToast(String(error))
    }).finally(() => { if (!disposed) setReady(true) })
    return () => { disposed = true; stopReconnect(); window.removeEventListener('resize', reportWidth) }
  }, [request])

  if (!ready) return <div className="main-placeholder">{t('shell.backendConnecting')}</div>

  return (
    <div className="app-shell detached" inert={workspaceBusy}>
      <MainArea headers="always" />
      <StatusBar />
      <WorkspaceRecoveryDialog />
      <SceneConflictDialog />
      <UnsavedLeaveDialog />
      <ShellDialogs />
    </div>
  )
}
