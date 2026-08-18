import { useEffect, useRef } from 'react'
import { useShellStore } from '../stores/shellStore'

/**
 * Tell the shell that leaving this screen would cost the writer work.
 *
 * A screen that holds edits in local state loses them the moment something
 * unmounts it, and the activity bar, the palette, a hotkey and a click in the
 * binder all do exactly that. Registering here puts the question in front of
 * the writer instead - once, from one place, however they tried to leave.
 *
 * `dirty` and `save` are read at the moment of leaving rather than captured at
 * registration, so a screen that goes clean and dirty again while it sits there
 * is answered on its current state.
 */
export function useUnsavedGuard(
  id: string,
  label: string,
  dirty: boolean,
  save: () => Promise<void>
): void {
  const latest = useRef({ dirty, save })
  latest.current = { dirty, save }

  useEffect(() => {
    const { registerUnsavedGuard, clearUnsavedGuard } = useShellStore.getState()
    registerUnsavedGuard({
      id,
      label,
      isDirty: () => latest.current.dirty,
      save: () => latest.current.save()
    })
    return () => clearUnsavedGuard(id)
  }, [id, label])
}
