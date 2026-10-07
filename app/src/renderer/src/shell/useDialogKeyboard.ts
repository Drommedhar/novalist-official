import { useEffect, useRef, type KeyboardEvent, type RefObject } from 'react'
import { useMotionPresent } from './MotionPresence'

export function restoreDialogFocus(dialog: HTMLElement | null, previous: HTMLElement | null): void {
  const active = document.activeElement
  if (previous?.isConnected && (active === document.body || dialog?.contains(active))) {
    previous.focus()
  }
}

export function useDialogAutoFocus(ref: RefObject<HTMLElement | null>): void {
  const present = useMotionPresent()
  useEffect(() => {
    if (!present) return
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const dialog = ref.current?.closest<HTMLElement>('[role="dialog"]') ?? ref.current
    ref.current?.focus()
    return () => restoreDialogFocus(dialog, previous)
  }, [present, ref])
}

/** Keyboard containment and focus restoration for the shared small dialogs. */
export function useDialogKeyboard(onCancel: () => void): {
  ref: RefObject<HTMLDivElement | null>
  onKeyDown(event: KeyboardEvent<HTMLDivElement>): void
} {
  const ref = useRef<HTMLDivElement>(null)
  const present = useMotionPresent()
  const controls = (): HTMLElement[] =>
    Array.from(
      ref.current?.querySelectorAll<HTMLElement>(
        'input:not(:disabled), button:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href], [tabindex="0"]'
      ) ?? []
    ).filter((el) => el.getClientRects().length > 0)

  useEffect(() => {
    if (!present) return
    const dialog = ref.current
    const previous = document.activeElement as HTMLElement | null
    controls()[0]?.focus()
    return () => {
      restoreDialogFocus(dialog, previous)
    }
  }, [present])

  return {
    ref,
    onKeyDown(event) {
      if (!present) return
      if (event.key === 'Escape') {
        event.preventDefault()
        event.stopPropagation()
        onCancel()
      } else if (event.key === 'Tab') {
        const items = controls()
        const first = items[0],
          last = items[items.length - 1]
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault()
          last?.focus()
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault()
          first?.focus()
        }
      }
    }
  }
}
