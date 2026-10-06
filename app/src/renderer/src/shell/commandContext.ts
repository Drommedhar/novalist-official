import { useEditorBridge } from '../stores/editorBridgeStore'
import { useProjectStore } from '../stores/projectStore'
import { useSettingsStore } from '../stores/settingsStore'
import { useShellStore } from '../stores/shellStore'
import type { EditorWindow } from '../views/editor/editorBridge'

export const shell = (): ReturnType<typeof useShellStore.getState> => useShellStore.getState()
export const project = (): ReturnType<typeof useProjectStore.getState> => useProjectStore.getState()

/** The editor showing the scene the writer is in, or null. */
export function liveEditor(): EditorWindow | null {
  return useEditorBridge.getState().editor
}

/** Wraps an editor call so a command is a no-op rather than a throw with none open. */
export function inEditor(command: (editor: EditorWindow) => void): () => void {
  return () => {
    const live = liveEditor()
    if (live) command(live)
  }
}

export const editorOpen = (): boolean => liveEditor() !== null
export const hasSelection = (): boolean =>
  liveEditor() !== null && useEditorBridge.getState().hasSelection
export const projectOpen = (): boolean => project().isLoaded
export const sceneOpen = (): boolean => {
  const state = project()
  return state.openChapterGuid !== null && state.openSceneId !== null
}

/** Applies one editor preference at whichever scope the writer is editing. */
export function updateEditorSetting(patch: Record<string, unknown>): void {
  const view = useSettingsStore.getState().view
  const scope = view?.overriddenSections?.editor ? 'project' : 'global'
  void useSettingsStore.getState().update(scope, patch)
}

/** Reads one boolean editor preference, defaulting to off. */
export function editorFlag(key: string): boolean {
  const effective = useSettingsStore.getState().view?.effective as
    | Record<string, unknown>
    | undefined
  return effective?.[key] === true
}

/** Flips one boolean editor preference. */
export function toggleEditorSetting(key: string): () => void {
  return () => updateEditorSetting({ [key]: !editorFlag(key) })
}
