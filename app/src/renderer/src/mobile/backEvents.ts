import type { KeyboardEvent as ReactKeyboardEvent } from 'react'

export function dismissOnAndroidBack(event: ReactKeyboardEvent, dismiss: () => void): void {
  if (window.novalist.platform !== 'android' || event.key !== 'Escape') return
  event.preventDefault()
  event.stopPropagation()
  dismiss()
}

export function dispatchAndroidBack(): boolean {
  const nativeDialog = document.querySelector<HTMLDialogElement>('dialog[open]')
  if (nativeDialog) {
    if (nativeDialog.dispatchEvent(new Event('cancel', { cancelable: true }))) nativeDialog.close()
    return true
  }
  const dialogs = document.querySelectorAll('[role="dialog"], [role="alertdialog"]')
  const dialog = dialogs.item(dialogs.length - 1)
  if (dialog) {
    const target = document.activeElement && dialog.contains(document.activeElement) ? document.activeElement : dialog
    target.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))
    return true
  }
  return !window.dispatchEvent(new Event('novalist-android-back', { cancelable: true }))
}
