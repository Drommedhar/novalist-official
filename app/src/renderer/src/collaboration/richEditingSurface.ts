import type { EditorWindow } from '../views/editor/editorBridge'
import type { MarkupEnvironment } from './sceneMarkup'

export interface RichEditingSurface extends MarkupEnvironment {
  root: HTMLElement
  getContent(): string
  getPlainText(): string
  flushPendingContentChange(): void
}

export function sceneEditorSurface(editor: EditorWindow): RichEditingSurface {
  const root = editor.document.getElementById('editor')
  if (!root) throw new Error('The prose editor is not ready.')
  return {
    root, document: root.ownerDocument,
    sanitizeSceneFragment: html => editor.sanitizeSceneFragment(html),
    toDisplayImages: content => editor.toDisplayImages(content),
    refreshLayout: () => editor.repaginatePageView(),
    getContent: () => editor.getContent(),
    getPlainText: () => editor.getPlainText(),
    flushPendingContentChange: () => editor.flushPendingContentChange()
  }
}
