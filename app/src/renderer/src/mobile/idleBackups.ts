import { rpc } from '../rpc/client'
import { listenForUserActivity } from './userActivity'

const IDLE_MS = 60_000

export function startIdleBackups(): () => void {
  let stopped = false
  let trigger = 'open'
  let timer: ReturnType<typeof setTimeout>
  let pending: AbortController | null = null

  const schedule = (): void => {
    window.clearTimeout(timer)
    if (!stopped) timer = window.setTimeout(() => { void run() }, IDLE_MS)
  }
  const run = async (): Promise<void> => {
    const controller = new AbortController()
    pending = controller
    const options = { signal: controller.signal }
    try {
      if (trigger === 'open' || await rpc.request<boolean>('backup/isDue', undefined, options)) {
        await rpc.request('backup/create', [trigger], options)
      }
      trigger = 'interval'
    } catch (error) {
      if (!controller.signal.aborted) console.error('[backup] Automatic backup failed', error)
    } finally {
      if (pending === controller) pending = null
      schedule()
    }
  }
  const activity = (): void => { pending?.abort(); schedule() }
  const stopListening = listenForUserActivity(activity)
  schedule()
  return () => {
    stopped = true
    window.clearTimeout(timer)
    pending?.abort()
    stopListening()
  }
}
