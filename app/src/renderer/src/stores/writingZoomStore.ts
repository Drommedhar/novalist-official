import { create } from 'zustand'

interface WritingZoomState {
  percent: number
  change(delta: number): void
}

export const useWritingZoomStore = create<WritingZoomState>((set) => ({
  percent: 100,
  change: (delta) => {
    if (!Number.isFinite(delta) || delta === 0) return
    set(state => ({ percent: Math.max(50, Math.min(300, state.percent + Math.sign(delta) * 10)) }))
  }
}))
