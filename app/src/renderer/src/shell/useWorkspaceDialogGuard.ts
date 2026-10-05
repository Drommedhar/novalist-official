import { useLayoutEffect } from 'react'

const drafts = new Set<symbol>()

/** Typed dialog drafts have no safe target after another window changes scope. */
export function hasWorkspaceDialogDraft(): boolean {
  return drafts.size > 0
}

export function useWorkspaceDialogGuard(active = true): void {
  useLayoutEffect(() => {
    if (!active) return
    const draft = Symbol('workspace dialog')
    drafts.add(draft)
    return () => { drafts.delete(draft) }
  }, [active])
}
