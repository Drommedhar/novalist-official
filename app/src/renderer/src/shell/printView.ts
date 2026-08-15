/**
 * Prints what the writer is looking at.
 *
 * A prose iframe prints itself: the editor and Manuscript mode each hold their
 * text in a document of their own, and printing the shell around them would
 * clip everything below the visible area. Every other view prints through the
 * shell's own print stylesheet, which drops the chrome and leaves the main
 * area - so a timeline, a plot grid or a calendar prints as it reads.
 */
export function printCurrentView(): void {
  // With the editor split in two, the pane the writer is in is the one they
  // mean; activeElement is the iframe itself while a frame has focus.
  const active = document.activeElement
  const frame =
    active instanceof HTMLIFrameElement && active.classList.contains('editor-frame')
      ? active
      : document.querySelector<HTMLIFrameElement>('.editor-frame')
  const inner = frame?.contentWindow
  if (inner) {
    inner.focus()
    inner.print()
    return
  }
  window.print()
}
