import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/** Launches the packaged .app (when present) and verifies the backend handshake. */

const PACKAGED_BIN =
  process.platform === 'win32'
    ? join('dist', 'win-unpacked', 'Novalist.exe')
    : process.platform === 'darwin'
      ? join('dist', `mac-${process.arch}`, 'Novalist.app', 'Contents', 'MacOS', 'Novalist')
      : join('dist', 'linux-unpacked', 'novalist')

test('packaged app boots and connects to its bundled backend', async () => {
  test.skip(!existsSync(PACKAGED_BIN), 'packaged app not built')
  test.setTimeout(120_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-pack-'))

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ executablePath: PACKAGED_BIN, env })
  const page = await app.firstWindow()

  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })
  await expect(page.locator('.start-card')).toBeVisible()

  await app.close()
})
