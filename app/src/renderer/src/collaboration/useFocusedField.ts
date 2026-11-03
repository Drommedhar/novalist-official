import { useEffect, useMemo, useState } from 'react'
import { FocusedField, type FieldBinding, type FieldView } from './FocusedField'
import { persistPendingWrite, registerPendingWrite, retainPendingWrite } from '../stores/pendingWrites'

const retainedFields = new Map<string, FocusedField>()

export function useFocusedField(binding: FieldBinding, value: string, saved?: () => Promise<void>) {
  const fieldKey = `${binding.sessionId}:${binding.itemId}`
  const controller = useMemo(() => retainedFields.get(fieldKey) ?? new FocusedField(binding, value), [fieldKey])
  const [view, setView] = useState<FieldView>(controller.snapshot)
  const key = controller.pendingKey
  const flush = async (preserveFocus = false): Promise<void> => {
    await controller.finish(preserveFocus)
    if (retainedFields.get(fieldKey) === controller) retainedFields.delete(fieldKey)
    await saved?.()
  }
  useEffect(() => {
    controller.onChange = setView
    const stop = registerPendingWrite(() => persistPendingWrite(key, flush))
    return () => {
      stop()
      controller.onChange = undefined
      retainedFields.set(fieldKey, controller)
      retainPendingWrite(key, flush)
    }
  }, [controller, key])
  useEffect(() => controller.stored(value), [controller, value])
  const blur = (): void => {
    controller.blur()
    void persistPendingWrite(key, () => flush(true)).catch(() => {})
  }
  const recover = (): void => {
    void persistPendingWrite(key, async () => { await controller.recover(); await flush() }).catch(() => {})
  }
  return { controller, view, blur, recover }
}
