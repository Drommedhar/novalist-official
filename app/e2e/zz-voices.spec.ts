import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

test('system voices reach the app', async () => {
  test.setTimeout(120_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-sv-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'
  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  const voices = await page.evaluate(() =>
    window.novalistRpc.request('voices/list') as Promise<
      { id: string; name: string; language: string }[]
    >
  )
  console.log('PROBE-COUNT ' + voices.length)
  console.log('PROBE-GERMAN ' + JSON.stringify(
    voices.filter((v) => v.language.startsWith('de')).slice(0, 4).map((v) => v.name)))
  await app.close()
})
