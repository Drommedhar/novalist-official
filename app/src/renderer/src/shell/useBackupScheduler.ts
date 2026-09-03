import { useEffect, useRef } from 'react'
import { rpc } from '../rpc/client'
import { useProjectStore } from '../stores/projectStore'

/** How often to ask the backend whether an interval backup is due. */
const POLL_MS = 60_000
let closeBackupHandledForQuit = false

/** Creates the normal close backup and waits for the backend to finish it. */
export async function createCloseBackup(): Promise<void> {
  if (!useProjectStore.getState().projectName) return
  try {
    await rpc.request('backup/create', ['close'])
  } catch {
    // Backups are best-effort. A failed archive must not trap a writer in the
    // application after their live project data has already been saved.
  }
}

/** Prevents beforeunload from starting the same close archive a second time. */
export function markCloseBackupHandledForQuit(): void {
  closeBackupHandledForQuit = true
}

/** Restores normal close-backup behavior when an installer handoff is rejected. */
export function clearCloseBackupHandledForQuit(): void {
  closeBackupHandledForQuit = false
}

/**
 * Drives automatic whole-project backups.
 *
 * The renderer owns the clock rather than the core, so a backup can never fire
 * for a project that is not actually open. The backend decides whether one is
 * due (it owns the interval setting and the last-archive timestamp); this only
 * asks and acts.
 *
 * Fires on project open, on an interval while it stays open, and once on window
 * close. Failures are swallowed: a backup that cannot be written must never
 * block writing or tear down the app on quit.
 */
export function useBackupScheduler(): void {
  const projectName = useProjectStore((s) => s.projectName)
  const openRef = useRef<string | null>(null)

  useEffect(() => {
    if (!projectName) {
      openRef.current = null
      return
    }

    // One "open" archive per project, not one per re-render.
    if (openRef.current !== projectName) {
      openRef.current = projectName
      void rpc.request('backup/create', ['open']).catch(() => {})
    }

    const timer = window.setInterval(() => {
      void (async () => {
        try {
          if (await rpc.request<boolean>('backup/isDue')) {
            await rpc.request('backup/create', ['interval'])
          }
        } catch {
          // Ignored on purpose - see the note above.
        }
      })()
    }, POLL_MS)

    const onUnload = (): void => {
      if (closeBackupHandledForQuit) {
        closeBackupHandledForQuit = false
        return
      }
      void createCloseBackup()
    }
    window.addEventListener('beforeunload', onUnload)

    return () => {
      window.clearInterval(timer)
      window.removeEventListener('beforeunload', onUnload)
    }
  }, [projectName])
}
