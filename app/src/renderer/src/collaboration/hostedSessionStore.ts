import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { useHostBridgeStore } from '../stores/hostBridgeStore'

interface HostedSession {
  sessionId: string
  projectId: string
  server: string
  connection: string
  isOwner: boolean
}

type Operation = 'create' | 'join' | 'invite' | 'end'
interface HostedSessionState {
  session: HostedSession | null
  busy: Operation | null
  invitation: string
  error: string
  presenceUnavailable: boolean
  presenceFailures: string[]
  reportPresence(sessionId: string, viewId: string, unavailable: boolean): void
  refresh(): Promise<void>
  run(operation: Operation, params?: unknown[]): Promise<void>
}

export const useHostedSessionStore = create<HostedSessionState>((set, get) => ({
  session: null,
  busy: null,
  invitation: '',
  error: '',
  presenceUnavailable: false,
  presenceFailures: [],
  reportPresence(sessionId, viewId, unavailable) {
    if (get().session?.sessionId !== sessionId) return
    const failures = get().presenceFailures.filter(view => view !== viewId)
    if (unavailable) failures.push(viewId)
    set({ presenceFailures: failures, presenceUnavailable: failures.length > 0 })
  },
  async refresh() {
    if (get().busy) return
    try {
      const session = await rpc.request<HostedSession | null>('collaboration/status')
      if (!get().busy) set({ session, ...(!session ? { invitation: '' } : {}),
        ...(session?.sessionId !== get().session?.sessionId ? { presenceFailures: [], presenceUnavailable: false } : {}) })
    } catch (error) { set({ error: String(error) }) }
  },
  async run(operation, params) {
    if (get().busy) return
    set({ busy: operation, error: '', invitation: '' })
    try {
      if (operation === 'invite') set({ invitation: await rpc.request<string>('collaboration/invite', params) })
      else if (operation === 'end') {
        await rpc.request('collaboration/end')
        set({ session: null, presenceFailures: [], presenceUnavailable: false })
      } else {
        const session = operation === 'create'
          ? await rpc.request<HostedSession>('collaboration/create', params)
          : await rpc.request<HostedSession>('collaboration/join', params)
        set({ session, presenceFailures: [], presenceUnavailable: false })
      }
    } catch (error) {
      const message = String(error)
      set({ error: message })
      useHostBridgeStore.getState().pushToast(message)
    }
    finally { set({ busy: null }) }
  }
}))
