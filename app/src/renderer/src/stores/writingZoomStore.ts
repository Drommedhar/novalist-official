import { create } from 'zustand'

interface WritingZoomState {
  percent: number
  initialized: boolean
  fitOnce(percent: number): void
  change(delta: number): void
}

export const useWritingZoomStore = create<WritingZoomState>((set) => ({
  percent: 100,
  initialized: false,
  fitOnce: (percent) => {
    if (!Number.isFinite(percent) || percent <= 0) return
    set(state => state.initialized ? state : {
      initialized: true,
      percent: Math.max(50, Math.min(300, Math.floor(percent)))
    })
  },
  change: (delta) => {
    if (!Number.isFinite(delta) || delta === 0) return
    set(state => ({ initialized: true, percent: Math.max(50, Math.min(300, state.percent + Math.sign(delta) * 10)) }))
  }
}))
