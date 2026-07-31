import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { MainArea } from './MainArea'
import { StatusBar } from './StatusBar'
import { useProjectStore } from '../stores/projectStore'
import { newLeaf, useShellStore, type MainView } from '../stores/shellStore'

/**
 * A window holding one view.
 *
 * The Codex on a second monitor while the manuscript stays where it is. It runs
 * the same renderer and gets its own channel to the same backend, so the view
 * inside is the real one rather than a picture of it - edits made here land in
 * the project like any other.
 *
 * No binder and no activity bar: a torn-off pane is one thing on purpose, and
 * navigation belongs to the window the writer is working in.
 */
export function DetachedPane({ view }: { view: MainView }): React.JSX.Element {
  const { t } = useTranslation()
  const [ready, setReady] = useState(false)

  useEffect(() => {
    // Its own connection and its own copy of the project state. Sharing the
    // main window's would mean one window's navigation moving the other's.
    void rpc
      .connect()
      .then(async () => {
        const recent = await rpc.request<{ path: string }[]>('project/recent').catch(() => [])
        const path = recent[0]?.path
        if (path) {
          const state = await rpc.request('project/open', [path])
          useProjectStore.getState().applyState(state as never)
        }
        // One pane, holding the view this window was opened for.
        useShellStore.setState({ panes: newLeaf(view), mainView: view })
      })
      .catch(() => {})
      .finally(() => setReady(true))
  }, [view])

  if (!ready) return <div className="main-placeholder">{t('shell.backendConnecting')}</div>

  return (
    <div className="app-shell detached">
      <MainArea />
      <StatusBar />
    </div>
  )
}
