import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { useHostBridgeStore } from '../stores/hostBridgeStore'

export interface SteamJoinRequest { id: string; link: string | null; friendName: string | null; error: string | null }
interface SteamInvitationState {
  pending: SteamJoinRequest | null
  pollError: string
  refresh(): Promise<void>
  receive(connectString: string): Promise<void>
  dismiss(id: string): Promise<void>
}

export const useSteamInvitations = create<SteamInvitationState>((set, get) => ({
  pending: null,
  pollError: '',
  async refresh() {
    try {
      const pending = await rpc.request<SteamJoinRequest | null>('collaboration/steam/pendingInvitation')
      set({ pending, pollError: '' })
    } catch (error) {
      const message = String(error)
      if (message !== get().pollError) useHostBridgeStore.getState().pushToast(message)
      set({ pollError: message })
    }
  },
  async receive(connectString) {
    await rpc.request('collaboration/steam/receiveInvitation', [connectString])
    await get().refresh()
  },
  async dismiss(id) {
    await rpc.request('collaboration/steam/dismissInvitation', [id])
    if (get().pending?.id === id) set({ pending: null })
  }
}))
