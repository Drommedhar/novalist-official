import { useEffect, useRef, type KeyboardEvent, type RefObject } from 'react'

/** Keyboard containment and focus restoration for the shared small dialogs. */
export function useDialogKeyboard(onCancel: () => void): {
  ref: RefObject<HTMLDivElement | null>
  onKeyDown(event: KeyboardEvent<HTMLDivElement>): void
} {
  const ref = useRef<HTMLDivElement>(null)
  const controls = (): HTMLElement[] =>
    Array.from(
      ref.current?.querySelectorAll<HTMLElement>(
        'input:not(:disabled), button:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href], [tabindex="0"]'
      ) ?? []
    ).filter((el) => el.getClientRects().length > 0)

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    controls()[0]?.focus()
    return () => {
      if (previous?.isConnected) previous.focus()
    }
  }, [])

  return {
    ref,
    onKeyDown(event) {
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
