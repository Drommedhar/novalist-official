import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'
import { REAL_PROJECT } from './realProject'

/**
 * The research the open scene is about, beside the scene.
 *
 * Research reached the writer in two places and neither was where they were
 * writing: the Research view, which means leaving the scene, and an entity's
 * Wiki article, which means already knowing what to look up. So the note saying
 * "check whether the bridge existed in 1755" sat filed correctly and unread
 * while the bridge got written.
 *
 * Asserted here because no unit test can: that the section appears in the
 * inspector for a real scene of a real book, that it says why each item is
 * there, that an unrelated note stays out of it, and that clicking one lands in
 * Research with that item open.
 */

test('the inspector shows the research this scene is about', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-sceneres-'))
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

  // Open a scene, and find out who the app thinks is in it. Guessing a name
  // from the manuscript would make this a test of the cast matcher instead.
  const opened = await page.evaluate(async () => {
    const project = window.novalistStores.project.getState()
    // The first scene that has anybody in it. Not every scene names a
    // character, and one that does not has nothing for research to match on.
    for (const chapter of project.chapters) {
      for (const scene of chapter.scenes) {
        const ctx = (await window.novalistRpc.request('context/analyze', [
          chapter.guid,
          scene.id
        ])) as { characters: { id: string; name: string }[] }
        if (ctx.characters.length > 0) {
          await project.openScene(chapter.guid, scene.id)
          return { chapterGuid: chapter.guid, sceneId: scene.id, character: ctx.characters[0] }
        }
      }
    }
    return { chapterGuid: '', sceneId: '', character: null as { id: string; name: string } | null }
  })
  // Narrowed once, so the assertions below read the value rather than the
  // maybe-value: without a character in a scene the rest of this proves nothing.
  const character = opened.character
  expect(character?.id, 'no character was found in any scene').toBeTruthy()
  if (!character) throw new Error('no character in the sample project')

  // One note about somebody in the scene, one about nobody.
  await page.evaluate(async (characterId: string) => {
    await window.novalistRpc.request('research/save', [
      null, 'Did the bridge exist in 1755?', 'Note', 'A question about the bridge.', [], [characterId]
    ])
    await window.novalistRpc.request('research/save', [
      null, 'Shipping lanes of the North Sea', 'Note', 'Unrelated.', [], []
    ])
  }, character.id)

  // Re-open so the inspector asks again.
  await page.evaluate(async ({ chapterGuid, sceneId }) => {
    await window.novalistStores.project.getState().openScene(chapterGuid, sceneId)
  }, opened)

  const section = page.locator('.ctx-section', { hasText: /Recherche|Research|资料/ }).first()
  await expect(section).toBeVisible({ timeout: 30_000 })
  await expect(section).toContainText('Did the bridge exist in 1755?')
  // The reason, so the writer knows why without opening it.
  await expect(section).toContainText(character.name)
  // And the note about nobody stays out; a suggestion list that shows
  // everything is the Research view in a narrower column.
  await expect(section).not.toContainText('Shipping lanes')

  await section.locator('.ctx-research-row').first().click()
  await expect(page.locator('.research-actions')).toBeVisible({ timeout: 15_000 })
  // With that item open, not merely the view. A suggestion that lands the
  // writer in a list they still have to search is barely better than a link.
  await expect(page.locator('.codex-body')).toContainText('Did the bridge exist in 1755?')

  await app.close()
})
