const MODAL = '[role="dialog"], [role="alertdialog"]'
const OVERLAY = '.dialog-overlay, .help-overlay, .mobile-sheet-overlay, [aria-modal="true"]'
const FOCUSABLE = 'button:not(:disabled), input:not(:disabled), textarea:not(:disabled), select:not(:disabled), a[href], [tabindex]:not([tabindex="-1"]), [contenteditable="true"]'

function controls(dialog: HTMLElement): HTMLElement[] {
  return [...dialog.querySelectorAll<HTMLElement>(FOCUSABLE)]
    .filter((element) => !element.closest('[inert]') && element.getClientRects().length > 0)
}

/** Shared by desktop, detached windows and mobile, including nested dialogs. */
export function installModalFocus(): () => void {
  let lastFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null
  let stack: { dialog: HTMLElement; previous: HTMLElement | null }[] = []
  const background = new Map<HTMLElement, boolean>()
  const restoreBackground = (): void => {
    for (const [element, inert] of background) element.inert = inert
    background.clear()
  }
  const focus = (dialog: HTMLElement): void => {
    dialog.tabIndex = -1
    ;(controls(dialog)[0] ?? dialog).focus()
  }
  const refresh = (): void => {
    restoreBackground()
    const dialogs = [...document.querySelectorAll<HTMLElement>(MODAL)].filter((dialog) =>
      dialog.closest(OVERLAY) && !dialog.closest('[data-motion-presence="closing"], [hidden]') && dialog.getClientRects().length > 0)
    const removed = stack.filter((entry) => !dialogs.includes(entry.dialog))
    stack = stack.filter((entry) => dialogs.includes(entry.dialog))
    for (const entry of removed.reverse()) {
      if (entry.previous?.isConnected && (document.activeElement === document.body || entry.dialog.contains(document.activeElement))) entry.previous.focus()
    }
    for (const dialog of dialogs) {
      if (stack.some((entry) => entry.dialog === dialog)) continue
      stack.push({ dialog, previous: dialog.contains(document.activeElement) ? lastFocused : document.activeElement instanceof HTMLElement ? document.activeElement : null })
      dialog.setAttribute('aria-modal', 'true')
      if (!dialog.contains(document.activeElement)) focus(dialog)
    }
    const active = stack.at(-1)?.dialog
    if (!active) return
    let branch: HTMLElement = active
    while (branch.parentElement) {
      for (const sibling of branch.parentElement.children) {
        if (!(sibling instanceof HTMLElement) || sibling === branch) continue
        background.set(sibling, sibling.inert)
        sibling.inert = true
      }
      branch = branch.parentElement
    }
  }
  const onKey = (event: KeyboardEvent): void => {
    const dialog = stack.at(-1)?.dialog
    if (!dialog || event.key !== 'Tab') return
    const items = controls(dialog)
    const first = items[0] ?? dialog
    const last = items.at(-1) ?? dialog
    if (!dialog.contains(document.activeElement) || (event.shiftKey ? document.activeElement === first : document.activeElement === last)) {
      event.preventDefault()
      ;(event.shiftKey ? last : first).focus()
    }
  }
  const onFocus = (event: FocusEvent): void => {
    const dialog = stack.at(-1)?.dialog
    if (dialog && event.target instanceof Node && !dialog.contains(event.target)) { focus(dialog); return }
    if (event.target instanceof HTMLElement) {
      const owner = event.target.closest(MODAL)
      if (!owner || stack.some((entry) => entry.dialog === owner)) lastFocused = event.target
    }
  }
  const observer = new MutationObserver(refresh)
  observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['hidden', 'data-motion-presence'] })
  document.addEventListener('keydown', onKey, true)
  document.addEventListener('focusin', onFocus)
  refresh()
  return () => {
    observer.disconnect()
    document.removeEventListener('keydown', onKey, true)
    document.removeEventListener('focusin', onFocus)
    restoreBackground()
    stack = []
  }
}
