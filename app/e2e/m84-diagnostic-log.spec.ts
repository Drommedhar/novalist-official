import { test, expect } from '@playwright/test'
import { existsSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { launchApp } from './harness'

test('diagnostic logging writes, opens, and clears the real log', async () => {
  const h = await launchApp('novalist-diagnostic-log-')
  const logs = join(h.workDir, 'settings', 'logs')
  let firstClosed = false
  let restarted: Awaited<ReturnType<typeof launchApp>> | null = null
  try {
    await h.page.evaluate(() =>
      window.novalistStores.shell.getState().openSettings('diagnostics')
    )

    // The checkbox is controlled by the settings model returned from the
    // backend, so its checked state lands after the RPC round-trip.
    await h.page.getByLabel('Diagnostic logging').click()
    await expect
      .poll(() =>
        existsSync(logs) ? readdirSync(logs).filter((file) => file.endsWith('.log')).length : 0
      )
      .toBe(1)

    // Keep the spec out of the real file manager. The IPC handlers close over
    // Electron's shell object, so replacing this one method records the same
    // path they would otherwise hand to the OS.
    await h.app.evaluate(({ shell }) => {
      ;(globalThis as unknown as { __openedLogPaths?: string[] }).__openedLogPaths = []
      shell.openPath = async (path: string) => {
        ;(globalThis as unknown as { __openedLogPaths: string[] }).__openedLogPaths.push(path)
        return ''
      }
    })

    await h.page.getByRole('button', { name: 'Open log folder' }).click()
    await h.page.getByRole('button', { name: 'Open current log' }).click()

    await expect
      .poll(() =>
        h.app.evaluate(
          () => (globalThis as unknown as { __openedLogPaths: string[] }).__openedLogPaths.length
        )
      )
      .toBe(2)
    const opened = await h.app.evaluate(
      () => (globalThis as unknown as { __openedLogPaths: string[] }).__openedLogPaths
    )
    expect(opened[0]).toBe(logs)
    expect(opened[1]).toBe(join(logs, readdirSync(logs)[0]))

    await h.page.getByRole('button', { name: 'Clear logs' }).click()
    await expect.poll(() => readdirSync(logs).filter((file) => file.endsWith('.log'))).toEqual([])

    // The opt-in is persisted. A fresh backend process must restore it early
    // enough for startup itself to be the first entry in a new log.
    await h.close()
    firstClosed = true
    restarted = await launchApp('novalist-diagnostic-log-restart-', {
      NOVALIST_SETTINGS_DIR: join(h.workDir, 'settings')
    })
    await expect
      .poll(() => readdirSync(logs).filter((file) => file.endsWith('.log')).length)
      .toBe(1)
  } finally {
    if (restarted) await restarted.close()
    if (!firstClosed) await h.close()
  }
})
