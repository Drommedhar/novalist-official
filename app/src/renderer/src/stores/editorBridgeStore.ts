import { create } from 'zustand'
import type { EditorWindow } from '../views/editor/editorBridge'

interface EditorBridgeState {
  /**
   * The editor showing the scene the writer is in, or null when none is open.
   *
   * The shell could not reach the editor at all. Panels outside the editor
   * pane - the footnotes and comments lists - wrote their changes to the
   * backend and had no way to tell the prose about them, so deleting a
   * footnote in the panel left its marker sitting in the scene.
   */
  editor: EditorWindow | null

  /**
   * Which scene that editor is showing. A stale bridge from a scene the writer
   * has left is worse than none: it would strip a marker out of the wrong
   * scene's prose.
   */
  sceneId: string | null
  hasSelection: boolean
  entityAtCaret: boolean

  /**
   * The two editor commands the frame around the editor owns rather than the
   * editor itself: asking for a link address, and starting or stopping the
   * system speech engine. Registered by EditorFrame while it is mounted.
   *
   * They are here so the command registry can reach them. A command that only
   * a button knows how to run is a command the palette cannot offer, which is
   * how "Link" and "Read aloud" stayed unreachable by name.
   */
  requestLink: (() => void) | null
  toggleReadAloud: (() => void) | null

  /**
   * Bumped whenever the open scene's footnotes or comments change, wherever the
   * change came from.
   *
   * The editor and the Footnotes panel both hold a copy of the same list and
   * neither told the other, so inserting a footnote in the prose left the panel
   * showing the list as it was before - the writer had to leave the scene and
   * come back to see the note they had just made.
   */
  annotationsRevision: number
  /** The same, for the suggested edits held in the scene's own prose. */
  suggestionsRevision: number

  register(editor: EditorWindow | null, sceneId: string | null): void
  /** Says the scene's annotations moved; every list of them reloads. */
  annotationsChanged(): void
  /** Says the scene's suggested edits moved; every list of them reloads. */
  suggestionsChanged(): void
  setContext(context: { hasSelection: boolean; entityAtCaret: boolean }): void
  /** EditorFrame publishes its own commands; null on unmount. */
  setFrameCommands(commands: {
    requestLink: (() => void) | null
    toggleReadAloud: (() => void) | null
  }): void

  /** True when the bridge is live and showing this scene. */
  isShowing(sceneId: string): boolean
}

export const useEditorBridge = create<EditorBridgeState>((set, get) => ({
  editor: null,
  sceneId: null,
  hasSelection: false,
  entityAtCaret: false,
  requestLink: null,
  toggleReadAloud: null,
  annotationsRevision: 0,
  suggestionsRevision: 0,

  register: (editor, sceneId) =>
    set({ editor, sceneId, ...(editor ? {} : { hasSelection: false, entityAtCaret: false }) }),
  setContext: (context) => set(context),
  setFrameCommands: (commands) => set(commands),
  annotationsChanged: () => set((s) => ({ annotationsRevision: s.annotationsRevision + 1 })),
  suggestionsChanged: () => set((s) => ({ suggestionsRevision: s.suggestionsRevision + 1 })),

  isShowing: (sceneId) => {
    const state = get()
    return state.editor != null && state.sceneId === sceneId
  }
}))
