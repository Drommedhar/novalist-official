import { useManuscriptStore, reportManuscriptClaims } from './manuscriptStore'
import type { SharedEditorStatus } from '../collaboration/SharedEditorSession'

export function reportSharedManuscript(generation: string, chapterGuid: string, sceneId: string, status: SharedEditorStatus, active: boolean): void {
  useManuscriptStore.setState(state => {
    const sharedEdits = { ...state.sharedEdits }
    if (status.pending || status.error) sharedEdits[generation] = { chapterGuid, sceneId, pending: status.pending, error: status.error !== null }
    else delete sharedEdits[generation]
    const wordCount = status.plainText.trim() ? status.plainText.trim().split(/\s+/).length : 0
    const contentChanged = active && state.sections.some(section => section.scenes.some(scene =>
      scene.sceneId === sceneId && (scene.html !== status.html || scene.wordCount !== wordCount)))
    if (!contentChanged && JSON.stringify(sharedEdits) === JSON.stringify(state.sharedEdits)) return state
    const sections = contentChanged ? state.sections.map(section => ({ ...section, scenes: section.scenes.map(scene =>
      scene.sceneId === sceneId && (scene.html !== status.html || scene.wordCount !== wordCount)
        ? { ...scene, html: status.html, wordCount } : scene) })) : state.sections
    return { sharedEdits, sections }
  })
  reportManuscriptClaims()
}
