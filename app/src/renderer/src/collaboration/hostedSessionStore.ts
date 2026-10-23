import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { useHostBridgeStore } from '../stores/hostBridgeStore'

interface HostedSession {
  sessionId: string
  projectId: string
  server: string
  connection: string
  isOwner: boolean
  recoveryPath: string | null
  mode: 'hosted' | 'tcp' | 'steam'
}

interface SavedHostedProject { server: string; projectId: string; recoveryPath: string | null }
export interface CollaborationTransferProgress {
  phase: 'scanning' | 'connecting' | 'comparing' | 'transferring' | 'installing' | 'complete'
  completedFiles: number
  totalFiles: number
  completedBytes: number
  totalBytes: number
  reusedFiles: number
}
type Operation = 'create' | 'join' | 'invite' | 'end' | 'resume' | 'copy' | 'peerHost' | 'peerJoin' | 'steamHost' | 'steamJoin'
interface HostedSessionState {
  session: HostedSession | null
  saved: SavedHostedProject | null
  copied: boolean
  busy: Operation | null
  invitation: string
  error: string
  presenceUnavailable: boolean
  presenceFailures: string[]
  transfer: CollaborationTransferProgress | null
  hostSynchronizing: boolean
  cancelling: boolean
  cancel(): Promise<void>
  reportPresence(sessionId: string, viewId: string, unavailable: boolean): void
  refresh(isCurrent?: () => boolean): Promise<void>
  run(operation: Operation, params?: unknown[]): Promise<void>
}

export const useHostedSessionStore = create<HostedSessionState>((set, get) => ({
  session: null,
  saved: null,
  copied: false,
  busy: null,
  invitation: '',
  error: '',
  presenceUnavailable: false,
  presenceFailures: [],
  transfer: null,
  hostSynchronizing: false,
  cancelling: false,
  async cancel() {
    if ((!get().busy && !get().hostSynchronizing) || get().cancelling) return
    set({ cancelling: true })
    try { await rpc.request('collaboration/cancel') }
    catch (error) { set({ error: String(error), cancelling: false }) }
  },
  reportPresence(sessionId, viewId, unavailable) {
    if (get().session?.sessionId !== sessionId) return
    const failures = get().presenceFailures.filter(view => view !== viewId)
    if (unavailable) failures.push(viewId)
    set({ presenceFailures: failures, presenceUnavailable: failures.length > 0 })
  },
  async refresh(isCurrent = () => true) {
    if (get().busy) return
    try {
      const [session, saved] = await Promise.all([
        rpc.request<HostedSession | null>('collaboration/status'),
        rpc.request<SavedHostedProject | null>('collaboration/saved')
      ])
      if (isCurrent() && !get().busy) set({ session, saved, ...(!session ? { invitation: '' } : {}),
        ...(session?.sessionId !== get().session?.sessionId ? { presenceFailures: [], presenceUnavailable: false } : {}) })
    } catch (error) { if (isCurrent()) set({ error: String(error) }) }
  },
  async run(operation, params) {
    if (get().busy) return
    set({ busy: operation, error: '', invitation: '', copied: false, transfer: null, cancelling: false })
    try {
      if (operation === 'invite') set({ invitation: await rpc.request<string>('collaboration/invite', params) })
      else if (operation === 'copy') {
        await rpc.request('collaboration/copy', params)
        set({ copied: true })
      }
      else if (operation === 'end') {
        await rpc.request('collaboration/end')
        set({ session: null, presenceFailures: [], presenceUnavailable: false })
      } else if (operation === 'resume') {
        set({ session: await rpc.request<HostedSession>('collaboration/resume'), presenceFailures: [], presenceUnavailable: false })
      } else if (operation === 'peerHost') {
        set({ session: await rpc.request<HostedSession>('collaboration/peer/host', params), presenceFailures: [], presenceUnavailable: false })
      } else if (operation === 'peerJoin') {
        set({ session: await rpc.request<HostedSession>('collaboration/peer/join', params), presenceFailures: [], presenceUnavailable: false })
      } else if (operation === 'steamHost') {
        set({ session: await rpc.request<HostedSession>('collaboration/steam/host', params), presenceFailures: [], presenceUnavailable: false })
      } else if (operation === 'steamJoin') {
        set({ session: await rpc.request<HostedSession>('collaboration/steam/join', params), presenceFailures: [], presenceUnavailable: false })
      } else {
        const session = operation === 'create'
          ? await rpc.request<HostedSession>('collaboration/create', params)
          : await rpc.request<HostedSession>('collaboration/join', params)
        set({ session, presenceFailures: [], presenceUnavailable: false })
      }
    } catch (error) {
      const message = String(error)
      set({ error: message, transfer: null })
      useHostBridgeStore.getState().pushToast(message)
    }
    finally { set({ busy: null, cancelling: false }) }
  }
}))
