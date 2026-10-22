import { EditorState } from '@codemirror/state'
import { EditorView, keymap } from '@codemirror/view'
import { defaultKeymap } from '@codemirror/commands'
import { SharedMarkdownSession } from '../../src/renderer/src/collaboration/SharedMarkdownSession'

interface Harness { start(state: number[]): void; exchange(): Promise<void> }
declare global { interface Window { markdownCollaboration: Harness } }

const sessions: SharedMarkdownSession[] = []
const pending: Uint8Array[][] = [[], []]
window.markdownCollaboration = {
  start(state) {
    for (let index = 0; index < 2; index++) {
      const session = new SharedMarkdownSession(new Uint8Array(state), {
        read: async () => new Uint8Array(state),
        write: async update => { pending[index].push(update) },
        subscribe: () => () => {}
      }, failed => { if (failed) throw new Error('The Markdown editor could not save its edits.') })
      const view = new EditorView({ parent: document.getElementById(`writer-${index}`)!,
        state: EditorState.create({ doc: session.text, extensions: [session.extension, keymap.of(defaultKeymap)] }) })
      session.attach(view)
      sessions.push(session)
    }
  },
  async exchange() {
    for (const session of sessions) await session.flush()
    for (let index = 0; index < 2; index++)
      for (const update of pending[index].splice(0)) sessions[1 - index].receive(update)
  }
}
