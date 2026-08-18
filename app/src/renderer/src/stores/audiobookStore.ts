import { create } from 'zustand'
import { rpc } from '../rpc/client'

/** How the render is going, or how it ended. */
export interface AudiobookStatus {
  /** idle, rendering, packaging, done, stopped or failed. */
  phase: string
  chapterIndex: number
  chapterCount: number
  chapterTitle: string
  segmentsDone: number
  segmentsTotal: number
  audioMs: number
  elapsedMs: number
  /** Lines the engine could not speak. A chapter with gaps is still a chapter,
   *  but the number has to reach the writer. */
  missing: number
  files: string[]
  deliveredFormat: string | null
  /** Why the delivery differs from what was asked for. */
  note: string | null
  error: string | null
}

interface AudiobookState {
  status: AudiobookStatus | null
  /** Starts polling, and keeps polling until the render ends. */
  watch: () => void
  /** Asks once - for the status bar on start-up, so a render begun before this
   *  window opened is still visible. */
  refresh: () => Promise<void>
}

/** How often a running render is asked how it is doing. */
const TICK_MS = 700

/**
 * What the audiobook render is doing, shared by the Export panel and the
 * status bar.
 *
 * Polled rather than pushed. The render outlives the request that started it -
 * it runs for hours - so there is no reply to attach progress to, and a backend
 * that answers one request at a time cannot hold a subscription open for a
 * night without holding everything else behind it.
 *
 * The polling stops the moment the render does. A timer left running after the
 * job ends is a request every second forever, which on a laptop is a battery
 * cost for nothing.
 */
export const useAudiobookStore = create<AudiobookState>((set, get) => {
  let timer: ReturnType<typeof setInterval> | null = null

  const stopPolling = (): void => {
    if (timer !== null) {
      clearInterval(timer)
      timer = null
    }
  }

  const ask = async (): Promise<void> => {
    try {
      const status = await rpc.request<AudiobookStatus>('audiobook/status')
      set({ status })
      if (status.phase !== 'rendering' && status.phase !== 'packaging') stopPolling()
    } catch {
      // A backend that is not there yet, or is restarting. The next tick asks
      // again; a failed poll is not a failed render.
      stopPolling()
    }
  }

  return {
    status: null,
    watch: (): void => {
      void ask()
      if (timer === null) timer = setInterval(() => void ask(), TICK_MS)
    },
    refresh: async (): Promise<void> => {
      await ask()
      // A render already under way when this window opened - after a reload, or
      // a second window. Without this the bar would sit idle through it.
      const phase = get().status?.phase
      if ((phase === 'rendering' || phase === 'packaging') && timer === null) {
        timer = setInterval(() => void ask(), TICK_MS)
      }
    }
  }
})
