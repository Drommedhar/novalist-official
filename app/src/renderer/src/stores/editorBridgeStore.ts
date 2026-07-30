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

  register(editor: EditorWindow | null, sceneId: string | null): void

  /** True when the bridge is live and showing this scene. */
  isShowing(sceneId: string): boolean
}

export const useEditorBridge = create<EditorBridgeState>((set, get) => ({
  editor: null,
  sceneId: null,

  register: (editor, sceneId) => set({ editor, sceneId }),

  isShowing: (sceneId) => {
    const state = get()
    return state.editor != null && state.sceneId === sceneId
  }
}))
