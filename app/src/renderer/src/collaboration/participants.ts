import { useEffect } from 'react'
import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { useHostedSessionStore } from './hostedSessionStore'

export interface Participant {
  deviceId: string
  viewId: string
  itemId: string
  name: string
  selection: { anchor: string; focus: string } | null
  sceneId: string | null
}

const empty: Participant[] = []
export const useParticipantStore = create<Record<string, Participant[]>>(() => ({}))
const polls = new Map<string, { references: number; stop(): void }>()

export function observeParticipants(sessionId: string): () => void {
  let poll = polls.get(sessionId)
  if (!poll) {
    let stopped = false
    let pending = false
    const refresh = async (): Promise<void> => {
      if (stopped || pending) return
      pending = true
      try {
        const participants = await rpc.request<Participant[]>('collaboration/participants', [sessionId])
        if (!stopped) {
          useParticipantStore.setState({ [sessionId]: participants })
          useHostedSessionStore.getState().reportPresence(sessionId, 'participants', false)
        }
      } catch {
        if (!stopped) {
          useParticipantStore.setState({ [sessionId]: empty })
          useHostedSessionStore.getState().reportPresence(sessionId, 'participants', true)
        }
      } finally { pending = false }
    }
    const timer = setInterval(() => void refresh(), 500)
    poll = { references: 0, stop() {
      stopped = true
      clearInterval(timer)
      useParticipantStore.setState(state => {
        const remaining = { ...state }
        delete remaining[sessionId]
        return remaining
      }, true)
      useHostedSessionStore.getState().reportPresence(sessionId, 'participants', false)
    } }
    polls.set(sessionId, poll)
    void refresh()
  }
  poll.references++
  return () => {
    if (--poll.references !== 0) return
    polls.delete(sessionId)
    poll.stop()
  }
}

export function useParticipants(sessionId: string | undefined): Participant[] {
  useEffect(() => sessionId ? observeParticipants(sessionId) : undefined, [sessionId])
  return useParticipantStore(state => sessionId ? state[sessionId] ?? empty : empty)
}
