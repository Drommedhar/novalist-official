import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * Picking a writing language has to reach the prose, not just the preview.
 *
 * The replacement pairs are what the editor types against, and the language
 * only ever seeded them - and only while the list was empty. So a writer who
 * picked German after their first launch was shown a preview promising low-9
 * quotes and got English ones in the manuscript, for good. Nothing in the
 * backend's own tests could catch that: the pairs were stored correctly, they
 * were simply never rewritten.
 */

test('picking a writing language changes the quotes that land in the prose', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-quotes-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await page.evaluate(async (parent) => {
    const rpc = window.novalistRpc
    let state = await rpc.request('project/create', [parent, 'Quotes', 'Book One'])
    state = await rpc.request('project/createChapter', ['One'])
    const chapters = (state as { chapters: { guid: string }[] }).chapters
    const guid = chapters[chapters.length - 1].guid
    state = await rpc.request('project/createScene', [guid, 'Opening'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)

  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await page.evaluate(() => window.novalistStores.settings.getState().load())

  // A fresh install is English, so this is the pair to move away from.
  await editor.click()
  await page.keyboard.type("'one'", { delay: 30 })
  await expect.poll(async () => editor.innerText(), { timeout: 15_000 }).toContain('“one”')

  await page.evaluate(async () => {
    await window.novalistStores.settings
      .getState()
      .update('global', { autoReplacementLanguage: 'de-low' })
  })

  // A new paragraph, because the open/close decision counts the quotes already
  // in this one - and English's opening quote is German's closing quote, so the
  // sentence above would read as a quotation still waiting to be closed.
  await editor.click()
  await page.keyboard.press('End')
  await page.keyboard.press('Enter')
  await page.keyboard.type("'zwei'", { delay: 30 })
  await expect.poll(async () => editor.innerText(), { timeout: 15_000 }).toContain('„zwei“')

  // The English pair is gone rather than sitting alongside the German one: two
  // conventions in one manuscript is what the language picker exists to avoid.
  const pairs = await page.evaluate(
    () =>
      JSON.stringify(
        window.novalistStores.settings.getState().view!.global.autoReplacements
      )
  )
  expect(pairs).toContain('„')
  expect(pairs).not.toContain('”')

  await app.close()
})
