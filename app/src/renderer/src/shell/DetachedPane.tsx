import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { MainArea } from './MainArea'
import { StatusBar } from './StatusBar'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { newLeaf, useShellStore, type MainView } from '../stores/shellStore'
import { useUiScaleStore } from '../stores/uiScaleStore'

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
  const [ready, setReady] = useState(false)

  useEffect(() => {
    useUiScaleStore.getState().apply()
    const reportWidth = (): void => useShellStore.getState().setShellMetrics(window.innerWidth)
    reportWidth()
    window.addEventListener('resize', reportWidth)
    // Its own connection and its own copy of the project state. Sharing the
    // main window's would mean one window's navigation moving the other's.
    void rpc
      .connect()
      .then(async () => {
        // The project the parent window is in. Falling back to the most recent
        // one is only for a window restored without it - opening a different
        // project than the one torn off is the bug this replaced.
        let path = request.projectPath
        if (!path) {
          const recent = await rpc.request<{ path: string }[]>('project/recent').catch(() => [])
          path = recent[0]?.path ?? null
        }
        // One pane, holding the view this window was opened for. Set before the
        // project loads so opening the scene below lands in it.
        const leaf = newLeaf(request.view)
        useShellStore.setState({ panes: leaf, activePaneId: leaf.id, mainView: request.view })
        if (!path) return
        const state = await rpc.request<ProjectStateDto>('project/open', [path])
        useProjectStore.getState().applyState(state)
        // The editor torn off its scene is a window with an editor in it, not a
        // window telling the writer to open a project they already have open.
        if (request.chapterGuid && request.sceneId) {
          await useProjectStore
            .getState()
            .openSceneIn(leaf.id, request.chapterGuid, request.sceneId)
        }
      })
      .catch(() => {})
      .finally(() => setReady(true))
    return () => window.removeEventListener('resize', reportWidth)
  }, [request])

  if (!ready) return <div className="main-placeholder">{t('shell.backendConnecting')}</div>

  return (
    <div className="app-shell detached">
      <MainArea headers="always" />
      <StatusBar />
    </div>
  )
}
