/** Internal desktop protocol. These types are not part of the extension SDK. */
export interface WorkspaceSnapshot {
  epoch: number
  state: {
    isLoaded: boolean
    projectName: string | null
    projectPath: string | null
    activeBookId: string | null
    books: { id: string; name: string }[]
    chapters: unknown[]
  }
  draftId: string | null
}

export interface WorkspacePreparation {
  token: string
  epoch: number
  reason: string
}

export interface WorkspaceChange extends WorkspaceSnapshot {
  token: string
}

export type WorkspaceEvent =
  | { phase: 'prepare'; preparation: WorkspacePreparation }
  | { phase: 'changed'; snapshot: WorkspaceChange }
  | { phase: 'aborted'; token: string; epoch: number }
  | { phase: 'resumed'; token: string; epoch: number }

/** Authenticated by the desktop broker, never accepted from renderer payloads. */
export function desktopRequestId(owner: number, epoch: number, sequence: number, token?: string): string {
  return `nl/${owner}/${epoch}/${sequence}/${token ?? '-'}`
}

export type CloseStage = 'flush' | 'backup' | 'abort'
