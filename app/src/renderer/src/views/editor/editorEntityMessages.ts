import { useOnboardingStore } from '../../stores/onboardingStore'
import type { EditorHandlerContext, EditorMessageHandlers } from './editorMessageContext'

export function createEditorEntityMessages(context: EditorHandlerContext): EditorMessageHandlers {
  const { setPendingAppend, setPendingEntity, peekRef } = context
  const showEntity = (key: string, message: Parameters<EditorMessageHandlers[string]>[0]): void => {
    const hit = context.entityIndexRef.current.get(key)
    if (!hit) return
    context.showHoverCard(
      hit,
      Number(message.x ?? 0),
      Number(message.y ?? 0),
      (message as { rect?: { left: number; top: number; right: number; bottom: number } }).rect ?? null
    )
    useOnboardingStore.getState().completeTip('focus-peek')
  }
  return {
    appendToEntityRequested: (message) => {
      setPendingAppend(String(message.text ?? ''))
    },
    mentionCreateRequested: (message) => {
      // A name typed after `@` that matched nothing: ask which kind of entity
      // to make, then swap the placeholder for a real mention.
      setPendingEntity({
        name: String(message.name ?? ''),
        pendingId: String(message.pendingId ?? '')
      })
    },
    entityMentionHover: (message) => {
      showEntity(String(message.entityId), message)
    },
    entityHover: (message) => {
      showEntity(String(message.alias ?? '').toLowerCase(), message)
    },
    entityExit: () => {
      // Debounced so moving the pointer onto the card doesn't dismiss it.
      peekRef.current.scheduleHide()
    }
  }
}
