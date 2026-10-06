import { createEditorReadyMessages } from './editorReadyMessages'
import { createEditorContentMessages } from './editorContentMessages'
import { createEditorAnnotationMessages } from './editorAnnotationMessages'
import { createEditorEntityMessages } from './editorEntityMessages'
import { createEditorActionMessages } from './editorActionMessages'
import { createEditorProofingMessages } from './editorProofingMessages'
import { createEditorMediaMessages } from './editorMediaMessages'
import type { EditorMessageHandler } from './editorBridge'
import type { EditorHandlerContext } from './editorMessageContext'

export function createEditorMessageHandlers(context: EditorHandlerContext): Map<string, EditorMessageHandler> {
  return new Map(Object.entries({
    ...createEditorReadyMessages(context),
    ...createEditorContentMessages(context),
    ...createEditorAnnotationMessages(context),
    ...createEditorEntityMessages(context),
    ...createEditorActionMessages(context),
    ...createEditorProofingMessages(context),
    ...createEditorMediaMessages(context)
  }))
}
