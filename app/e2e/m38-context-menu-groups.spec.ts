import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * A context-menu group whose every row needs a selection is not offered when
 * there is none.
 *
 * The Codex group holds exactly two rows and both act on selected text, so
 * with the caret merely parked the flyout opened onto two dead entries. The
 * group name says nothing about wanting a selection, so it read as broken
 * rather than as unavailable. Individual rows still grey out - that is what
 * keeps a menu the same shape from one open to the next - but a group with
 * nothing live in it is dropped.
 */
test('a group with no usable row is not offered, and returns with a selection', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-ctxgroup-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  await evaluateWhenReady(page, async (parent) => {
    const rpc = window.novalistRpc
    let state = await rpc.request('project/create', [parent, 'Menus', 'Book One'])
    state = await rpc.request('project/createChapter', ['Chapter One'])
    const chapters = (state as { chapters: { guid: string }[] }).chapters
    const guid = chapters[chapters.length - 1].guid
    state = await rpc.request('project/createScene', [guid, 'A scene'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)

  await expect(page.locator('.binder-scene-row')).toHaveCount(1, { timeout: 20_000 })
  await page.locator('.binder-scene-row').first().click()

  const frame = page.frameLocator('iframe').first()
  const editor = frame.locator('#editor')
  await editor.click()
  await editor.pressSequentially('Liam Calder walked to the window.')

  const groupNames = async (): Promise<string[]> =>
    frame.locator('#context-menu .cm-parent > .cm-item > span').allTextContents()

  // Caret only. Scene survives - splitting and inserting an image need no
  // selection - and Codex, which is selection-only throughout, is absent.
  await editor.click({ button: 'right' })
  await expect(frame.locator('#context-menu.visible')).toBeVisible()
  expect(await groupNames()).toEqual(['Scene'])
  await page.keyboard.press('Escape')

  // With prose selected the group comes back, and its rows are live.
  await page.keyboard.press('Control+a')
  await editor.click({ button: 'right' })
  await expect(frame.locator('#context-menu.visible')).toBeVisible()
  expect(await groupNames()).toEqual(['Scene', 'Codex'])
  await expect(
    frame.locator('#context-menu .cm-parent', { hasText: 'Codex' }).locator('.cm-item.disabled')
  ).toHaveCount(0)

  await app.close()
})
