import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'
import { enterWriting } from './harness'
import { REAL_PROJECT } from './realProject'

/**
 * Paragraph dimming: with the setting on, the paragraph the caret is in stays
 * fully opaque and the rest go faint. The reported symptom was that nothing
 * happened at all.
 */

test('compose dimming marks the caret paragraph and dims the rest', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-dim-'))
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

  // An opened project lands on the Dashboard; the binder and editor are Write's.
  await enterWriting(page)

  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await expect
    .poll(async () => (await editor.innerText()).trim().length, { timeout: 15_000 })
    .toBeGreaterThan(20)

  // Three paragraphs of our own, so the test does not depend on which scene the
  // fixture project happens to open with.
  await editor.click()
  await page.keyboard.press('Control+A')
  await page.keyboard.type('One')
  await page.keyboard.press('Enter')
  await page.keyboard.type('Two')
  await page.keyboard.press('Enter')
  await page.keyboard.type('Three')

  // Turn the setting on the way Settings does.
  await page.evaluate(async () => {
    await window.novalistStores.settings.getState().update('global', { composeDimming: true })
  })

  // Put the caret in the second paragraph, as a writer would. Page view wraps
  // the paragraphs in .nv-page, which is exactly what the bug was about, so the
  // selector has to reach through it.
  const paragraphs = page
    .frameLocator('.editor-frame')
    .locator('#editor > :not(.nv-page), #editor > .nv-page > *')
  await expect.poll(async () => paragraphs.count(), { timeout: 10_000 }).toBeGreaterThan(1)
  await paragraphs.nth(1).click()

  // Polled rather than read once: the rule carries a 120ms transition, so an
  // immediate read catches the focused paragraph still on its way to opaque.
  const read = (): Promise<{
    bodyHasClass: boolean
    focused: number
    opacities: string[]
  } | null> =>
    page.evaluate(() => {
      const frame = document.querySelector<HTMLIFrameElement>('.editor-frame')
      const doc = frame?.contentDocument
      if (!doc) return null
      const blocks = Array.from(
        doc.querySelectorAll('#editor > :not(.nv-page), #editor > .nv-page > *')
      )
      return {
        bodyHasClass: doc.body.classList.contains('dim-others'),
        focused: blocks.filter((b) => b.classList.contains('nv-focus-block')).length,
        opacities: blocks.map((b) => doc.defaultView!.getComputedStyle(b).opacity)
      }
    })

  // Polled as one snapshot rather than polled-then-re-read: the two reads can
  // straddle a transition, and the second would catch the lit paragraph still
  // on its way up. Compared with a tolerance for the same reason.
  const lit = (o: string): boolean => Number(o) > 0.9
  const faint = (o: string): boolean => Number(o) < 0.5

  let state: Awaited<ReturnType<typeof read>> = null
  await expect
    .poll(
      async () => {
        state = await read()
        if (!state) return false
        // Exactly the caret's paragraph is lit and the rest are faint. Nothing
        // lit at all was the reported symptom.
        return (
          state.bodyHasClass &&
          state.focused === 1 &&
          state.opacities.some(lit) &&
          state.opacities.some(faint)
        )
      },
      { timeout: 10_000 }
    )
    .toBe(true)

  expect(state).not.toBeNull()

  await app.close()
})
