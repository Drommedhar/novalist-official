import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync, readFileSync, readdirSync, statSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * The exposé view against the real app: typing updates the Normseiten counts,
 * a character limit turns the counter red without blocking input, and the text
 * autosaves to the book's exposé file on disk.
 */

function findFile(root: string, name: string): string | null {
  for (const entry of readdirSync(root)) {
    const full = join(root, entry)
    if (statSync(full).isDirectory()) {
      const hit = findFile(full, name)
      if (hit) return hit
    } else if (entry === name) {
      return full
    }
  }
  return null
}

test('exposé counts, warns past the limit, and saves to disk', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-expose-'))

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await evaluateWhenReady(page, async (dir) => {
    const state = await window.novalistRpc.request('project/create', [
      dir,
      'Expose Novel',
      'Book One'
    ])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)

  // The activity bar must offer the view — a view without a rail button is invisible.
  await page.locator('.activity-bar-item[data-view="expose"]').click()
  await expect
    .poll(async () => page.evaluate(() => window.novalistStores.shell.getState().mainView))
    .toBe('expose')

  const editor = page.frameLocator('.expose-editor .editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })

  // A short character budget so a single typed sentence overshoots it.
  await page.locator('#expose-char-limit').fill('10')
  await page.locator('#expose-char-limit').blur()

  await editor.click()
  await page.keyboard.type('Eine Heldin bricht auf und kehrt veraendert zurueck.')

  // Counts land on the measure beat; the character counter flags the overrun.
  await expect(page.locator('.expose-counter.is-over')).toBeVisible({ timeout: 10_000 })
  await expect(page.locator('.expose-counter')).toHaveCount(2)

  // Typing is never blocked by the limit.
  const typed = await editor.innerText()
  expect(typed).toContain('kehrt veraendert zurueck')

  // Autosave (2s) writes the exposé next to the book.
  await page.waitForTimeout(4000)
  const exposeFile = findFile(join(workDir, 'Expose Novel'), 'Expose.novalist')
  expect(exposeFile, 'exposé file not written').toBeTruthy()
  expect(readFileSync(exposeFile!, 'utf8')).toContain('kehrt veraendert zurueck')

  // The limit survives a reload of the project metadata.
  const limits = (await page.evaluate(() =>
    window.novalistRpc.request('expose/get')
  )) as { charLimit: number; pages: number }
  expect(limits.charLimit).toBe(10)
  expect(limits.pages).toBe(1)

  await app.close()
})

/**
 * The Normseiten export reads heading paragraphs off the `nv-style-*` class.
 * Editing the exposé must not strip it, or a document's section headings would
 * silently flatten into body text the first time the writer touches it.
 */
test('exposé editing preserves heading paragraph styles', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-expose-rt-'))

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await evaluateWhenReady(page, async (dir) => {
    const state = await window.novalistRpc.request('project/create', [dir, 'Styled', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
    await window.novalistRpc.request('expose/save', [
      '<p class="nv-style-heading">Titel</p><p></p><p class="nv-style-subheading">Handlung</p><p>Erste Zeile.</p>'
    ])
  }, workDir)

  await page.locator('.activity-bar-item[data-view="expose"]').click()
  const editor = page.frameLocator('.expose-editor .editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await expect(editor).toContainText('Erste Zeile.')

  // Type at the very end and let the 2s autosave round-trip the whole document.
  await editor.click()
  await page.keyboard.press('Control+End')
  await page.keyboard.type(' Zweiter Satz.')
  await page.waitForTimeout(4000)

  const exposeFile = findFile(join(workDir, 'Styled'), 'Expose.novalist')
  expect(exposeFile, 'exposé file not written').toBeTruthy()
  const saved = readFileSync(exposeFile!, 'utf8')
  expect(saved).toContain('Zweiter Satz.')
  expect(saved, 'heading style stripped on edit').toContain('nv-style-heading')
  expect(saved, 'subheading style stripped on edit').toContain('nv-style-subheading')

  // TITEL / blank / HANDLUNG / blank / the body line.
  const state = (await page.evaluate(() => window.novalistRpc.request('expose/get'))) as {
    lines: number
  }
  expect(state.lines).toBe(5)

  await app.close()
})

/**
 * The paragraph-style buttons are the only way to mark a heading, so they have
 * to apply the class, reflect the caret's current style, and clear back to body.
 */
test('exposé paragraph-style buttons write and clear heading styles', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-expose-style-'))

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await evaluateWhenReady(page, async (dir) => {
    const state = await window.novalistRpc.request('project/create', [dir, 'Styling', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
    await window.novalistRpc.request('expose/save', ['<p>Handlung</p><p>Erste Zeile.</p>'])
  }, workDir)

  await page.locator('.activity-bar-item[data-view="expose"]').click()
  const editor = page.frameLocator('.expose-editor .editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await expect(editor).toContainText('Handlung')

  // Put the caret in the first paragraph and mark it as a section heading.
  await editor.locator('p').first().click()
  await page.locator('.expose-style-btn[data-style="subheading"]').click()

  // The button reflects the caret's paragraph...
  await expect(page.locator('.expose-style-btn[data-style="subheading"]')).toHaveAttribute(
    'aria-pressed',
    'true'
  )
  // ...and the style reaches disk on the next autosave.
  await page.waitForTimeout(4000)
  const exposeFile = findFile(join(workDir, 'Styling'), 'Expose.novalist')
  expect(readFileSync(exposeFile!, 'utf8')).toContain('nv-style-subheading')

  // A section heading brackets itself with blank lines: heading, blank, body.
  await expect
    .poll(
      async () =>
        (
          (await page.evaluate(() => window.novalistRpc.request('expose/get'))) as {
            lines: number
          }
        ).lines,
      { timeout: 10_000 }
    )
    .toBe(3)

  // Clearing back to body drops the class again.
  await editor.locator('p').first().click()
  await page.locator('.expose-style-btn[data-style="body"]').click()
  await page.waitForTimeout(4000)
  expect(readFileSync(exposeFile!, 'utf8')).not.toContain('nv-style-')

  await app.close()
})
