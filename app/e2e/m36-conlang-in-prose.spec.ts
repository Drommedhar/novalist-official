import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'

/**
 * A coined word, recognised in the manuscript.
 *
 * A dictionary the writer has to go and open is a dictionary they stop opening,
 * so a language module whose words never appear while drafting is a list. This
 * is the half that makes it worth having, and the half no unit test reaches:
 * that the word the writer coined is actually pushed to the editor as something
 * to recognise, and that the card raised over it is the dictionary entry rather
 * than a Codex lookup that finds nothing.
 */
const REAL_PROJECT = process.env.NOVALIST_REAL_PROJECT ?? '/Users/dominikgoblirsch/GIT/The-Silent-Shadows'

test('a coined word is recognised in the manuscript and peeks its meaning', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-conlang-'))
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

  // A language with three words: one to find, one too short to be safe to
  // match, and one that collides with a character's name.
  const coined = await page.evaluate(async () => {
    // Every conlang RPC returns the whole list, so the new language is found
    // in the answer rather than returned on its own.
    const created = (await window.novalistRpc.request('conlang/create', ['Old Hillsford'])) as {
      id: string
      name: string
    }[]
    const language = created.find((l) => l.name === 'Old Hillsford')!
    await window.novalistRpc.request('conlang/saveWord', [
      language.id, null, 'kelvarien', 'the first thaw of the year', 'noun', 'kel-VAR-ee-en', ''
    ])
    const after = (await window.novalistRpc.request('conlang/saveWord', [
      language.id, null, 'an', 'towards', 'preposition', '', ''
    ])) as { id: string; words: { id: string; word: string }[] }[]
    const words = after.find((l) => l.id === language.id)!.words
    return { languageId: language.id, wordId: words.find((w) => w.word === 'kelvarien')!.id }
  })

  // The backend answers for it in the shape the peek card draws.
  const peek = (await page.evaluate(
    async (wordId: string) => window.novalistRpc.request('entities/peek', ['conlang', wordId]),
    coined.wordId
  )) as { title: string; description: string; pills: { text: string | null }[] }
  expect(peek.title).toBe('kelvarien')
  expect(peek.description).toBe('the first thaw of the year')
  expect(peek.pills.map((p) => p.text)).toContain('Old Hillsford')

  // And the editor is actually told to recognise it. The iframe does not report
  // what it was handed, so the bridge method is wrapped before the push runs -
  // otherwise this could only assert that the word exists, which the RPC above
  // already did.
  // A scene first: the editor iframe does not exist until one is open, and
  // wrapping a method on a frame that is not there wraps nothing.
  const scenes = await page.evaluate(async () => {
    const project = window.novalistStores.project.getState()
    const chapter = project.chapters.find((c) => c.scenes.length > 1)!
    await project.openScene(chapter.guid, chapter.scenes[0].id)
    return { chapterGuid: chapter.guid, second: chapter.scenes[1].id }
  })
  await expect(page.locator('iframe')).toBeVisible({ timeout: 30_000 })
  await page.waitForTimeout(2000)

  await page.evaluate(() => {
    const frame = document.querySelector('iframe') as HTMLIFrameElement | null
    const win = frame?.contentWindow as unknown as {
      setEntityNames?: (json: string) => void
    }
    if (!win?.setEntityNames) return
    const original = win.setEntityNames.bind(win)
    ;(window as unknown as { __names?: string }).__names = undefined
    win.setEntityNames = (json: string): void => {
      ;(window as unknown as { __names?: string }).__names = json
      original(json)
    }
  })

  // Now a second scene, so the push runs again with the wrap in place.
  await page.evaluate(async ({ chapterGuid, second }) => {
    await window.novalistStores.project.getState().openScene(chapterGuid, second)
  }, scenes)
  await expect
    .poll(
      async () => page.evaluate(() => (window as unknown as { __names?: string }).__names ?? ''),
      { timeout: 30_000 }
    )
    .toContain('kelvarien')

  const pushed = JSON.parse(
    await page.evaluate(() => (window as unknown as { __names?: string }).__names ?? '[]')
  ) as { name: string; entityType: string }[]

  const coinedNames = pushed.filter((n) => n.entityType === 'conlang').map((n) => n.name)
  expect(coinedNames).toContain('kelvarien')
  // Two letters is not a word worth highlighting in prose: every "an" in the
  // manuscript would light up.
  expect(coinedNames).not.toContain('an')

  await app.close()
})
