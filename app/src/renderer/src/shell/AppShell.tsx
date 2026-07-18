import { useEffect, useMemo } from 'react'
import { Binder } from './Binder'
import { CommandPalette } from './CommandPalette'
import { FindReplaceDialog } from './FindReplaceDialog'
import { buildDefaultHotkeys, installHotkeys } from './hotkeys'
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
  const findReplaceOpen = useShellStore((s) => s.findReplaceOpen)
  const commandPaletteOpen = useShellStore((s) => s.commandPaletteOpen)
  const hotkeys = useMemo(() => buildDefaultHotkeys(), [])

  useEffect(() => {
    rpc.onReconnected(() => void hydrate())
    void rpc.connect().then(hydrate)
  }, [])

  useEffect(() => installHotkeys(hotkeys), [hotkeys])

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
      {findReplaceOpen && (
        <FindReplaceDialog onClose={() => useShellStore.getState().setFindReplaceOpen(false)} />
      )}
      {commandPaletteOpen && (
        <CommandPalette
          actions={hotkeys}
          onClose={() => useShellStore.getState().setCommandPaletteOpen(false)}
        />
      )}
    </div>
  )
}
