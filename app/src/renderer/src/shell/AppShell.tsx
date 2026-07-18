import { useEffect } from 'react'
import { Binder } from './Binder'
import { Inspector } from './Inspector'
import { Toolbar } from './Toolbar'
import { StatusBar } from './StatusBar'
import { MainArea } from './MainArea'
import { StartScreen } from './StartScreen'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { rpc } from '../rpc/client'
import type { PingResult } from '../rpc/contract'
import './shell.css'

async function hydrate(): Promise<void> {
  const ping = await rpc.request<PingResult>('system/ping')
  useShellStore.getState().setBackendVersion(ping.version)
  const state = await rpc.request<ProjectStateDto>('project/getState')
  useProjectStore.getState().applyState(state)
  await useProjectStore.getState().loadRecents()
}

export function AppShell(): React.JSX.Element {
  const binderVisible = useShellStore((s) => s.binderVisible)
  const inspectorVisible = useShellStore((s) => s.inspectorVisible)
  const isLoaded = useProjectStore((s) => s.isLoaded)
  const recentProjects = useProjectStore((s) => s.recentProjects)
  const openProject = useProjectStore((s) => s.openProject)
  const pickAndOpenProject = useProjectStore((s) => s.pickAndOpenProject)

  useEffect(() => {
    rpc.onReconnected(() => void hydrate())
    void rpc.connect().then(hydrate)
  }, [])

  return (
    <div className="shell">
      <Toolbar />
      <div className="shell-body">
        {isLoaded ? (
          <>
            {binderVisible && <Binder />}
            <MainArea />
            {inspectorVisible && <Inspector />}
          </>
        ) : (
          <StartScreen
            recentProjects={recentProjects}
            onOpenPath={(path) => void openProject(path)}
            onPickProject={() => void pickAndOpenProject()}
          />
        )}
      </div>
      <StatusBar />
    </div>
  )
}
