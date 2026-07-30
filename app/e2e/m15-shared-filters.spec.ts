import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'

/**
 * One filter model, shared live. The Manuscript status filter and the
 * Timeline's character filter were unrelated local state, so narrowing to one
 * thread meant setting it again in each view and losing it on navigation.
 */
const REAL_PROJECT = process.env.NOVALIST_REAL_PROJECT ?? '/Users/dominikgoblirsch/GIT/The-Silent-Shadows'

test('a filter set in one view is still set in the next', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-filter-'))
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

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
  await expect(page.locator('.filter-bar')).toBeVisible({ timeout: 20_000 })

  // Narrow by status in the Manuscript, the way a writer would.
  await page.locator('.filter-bar select').first().selectOption('Final')

  // The manuscript's own filter follows the shared one rather than being a
  // second answer to the same question.
  await expect
    .poll(async () => page.locator('.filter-bar select').first().inputValue(), { timeout: 10_000 })
    .toBe('Final')

  // Still narrowed after moving to the Timeline, which is the whole point.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('timeline'))
  await expect(page.locator('.filter-bar')).toBeVisible({ timeout: 20_000 })
  expect(await page.locator('.filter-bar select').first().inputValue()).toBe('Final')

  // And it survives being named and re-applied.
  await page.locator('.filter-presets input').fill('Final pass')
  await page.locator('.filter-presets input').press('Enter')
  await page.locator('.filter-bar select').first().selectOption('')
  await page.getByRole('button', { name: 'Final pass' }).click()
  expect(await page.locator('.filter-bar select').first().inputValue()).toBe('Final')

  await app.close()
})
