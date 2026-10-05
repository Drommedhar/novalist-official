interface BookLoadScope {
  isLoaded: boolean
  projectPath: string | null
  activeBookId: string | null
  activeDraftId: string | null
  workspaceEpoch: number
  workspaceBusy: boolean
}

/** A read started for one book must not publish into a later workspace.
 * Requests rejected during a transition are obsolete; errors for the current
 * book still reach the caller. */
export async function loadBookScoped<T>(
  scope: () => BookLoadScope,
  request: () => Promise<T>,
  apply: (value: T) => void
): Promise<void> {
  const initial = scope()
  if (!initial.isLoaded || initial.workspaceBusy) return
  const isCurrent = (): boolean => {
    const current = scope()
    return current.isLoaded && !current.workspaceBusy &&
      current.workspaceEpoch === initial.workspaceEpoch &&
      current.projectPath === initial.projectPath &&
      current.activeBookId === initial.activeBookId &&
      current.activeDraftId === initial.activeDraftId
  }
  try {
    const value = await request()
    if (isCurrent()) apply(value)
  } catch (error) {
    if (isCurrent()) throw error
  }
}
