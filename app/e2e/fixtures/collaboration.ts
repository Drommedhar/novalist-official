import { CollaborativeScene } from '../../src/renderer/src/collaboration/CollaborativeScene'
import type { EditorWindow } from '../../src/renderer/src/views/editor/editorBridge'

export interface CollaborationHarness {
  start(state?: number[]): void
  state(): number[]
  receive(update: number[]): void
  drain(): number[][]
  errors(): string[]
  presence(): { anchor: number[]; focus: number[] } | null
  showPresence(selection: { anchor: number[]; focus: number[] }): void
}

declare global {
  interface Window { collaboration: CollaborationHarness }
}

const editor = window as unknown as EditorWindow
let scene: CollaborativeScene
let updates: number[][] = []
const errors: string[] = []
window.collaboration = {
  start(state) {
    scene = new CollaborativeScene(editor, {
      initialState: state && new Uint8Array(state),
      onUpdate: update => updates.push(Array.from(update)),
      onError: error => errors.push(error.message)
    })
  },
  state: () => Array.from(scene.encodeState()),
  errors: () => errors.splice(0),
  receive: update => scene.receive(new Uint8Array(update)),
  presence: () => scene.getSelection(),
  showPresence: selection => scene.showParticipant({ id: 'bea', name: 'Bea', selection }),
  drain() {
    scene.capture()
    const pending = updates
    updates = []
    return pending
  }
}
