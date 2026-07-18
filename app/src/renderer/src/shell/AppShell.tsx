import { useEffect } from 'react'
import { Binder } from './Binder'
import { Inspector } from './Inspector'
import { Toolbar } from './Toolbar'
import { StatusBar } from './StatusBar'
import { MainArea } from './MainArea'
import { useShellStore } from '../stores/shellStore'
import { rpc } from '../rpc/client'
import type { PingResult } from '../rpc/contract'
import './shell.css'

async function hydrate(): Promise<void> {
  const result = await rpc.request<PingResult>('system/ping')
  useShellStore.getState().setBackendVersion(result.version)
}

export function AppShell(): React.JSX.Element {
  const binderVisible = useShellStore((s) => s.binderVisible)
  const inspectorVisible = useShellStore((s) => s.inspectorVisible)

  useEffect(() => {
    rpc.onReconnected(() => void hydrate())
    void rpc.connect().then(hydrate)
  }, [])

  return (
    <div className="shell">
      <Toolbar />
      <div className="shell-body">
        {binderVisible && <Binder />}
        <MainArea />
        {inspectorVisible && <Inspector />}
      </div>
      <StatusBar />
    </div>
  )
}
