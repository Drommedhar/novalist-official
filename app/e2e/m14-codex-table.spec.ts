import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'

/**
 * The Codex edited one form at a time, so filing forty characters into their
 * houses meant forty round trips through the detail pane. The table is the
 * shape that work actually has - and an edit in a cell has to reach the file.
 */
const REAL_PROJECT = process.env.NOVALIST_REAL_PROJECT ?? '/Users/dominikgoblirsch/GIT/The-Silent-Shadows'

test('the codex table edits an entry in place', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-table-'))
  const projectCopy = join(workDir, 'project')
  copyProject(REAL_PROJECT, projectCopy)
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })
  await evaluateWhenReady(page, async (root) => {
    const state = await window.novalistRpc.request('project/open', [root])
    window.novalistStores.project.getState().applyState(state as never)
  }, projectCopy)

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
  await expect(page.locator('.codex-tabs')).toBeVisible({ timeout: 20_000 })

  // Into the table, which is a mode of the Codex rather than a view of its own.
  // By class, not by label: a project carries its own writing language, and
  // this one is German - so looking for a button called "Table" found nothing
  // and the failure read as a missing control rather than a missing word.
  await page.locator('.codex-tab-table').click()
  await expect(page.locator('.codex-table')).toBeVisible({ timeout: 15_000 })

  const firstGroupCell = page.locator('.codex-table tbody tr').first().locator('input').last()
  await firstGroupCell.fill('House Raven')
  await firstGroupCell.blur()

  // The edit is in the file, not just the cell.
  await expect
    .poll(
      async () =>
        page.evaluate(async () => {
          const rows = (await window.novalistRpc.request('entities/list', [
            window.novalistStores.codex.getState().entityType
          ])) as { group: string | null }[]
          return rows.some((r) => r.group === 'House Raven')
        }),
      { timeout: 10_000 }
    )
    .toBe(true)

  await app.close()
})
