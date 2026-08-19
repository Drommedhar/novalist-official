import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'
import { dismissTour } from './harness'

/**
 * A character who does not sound the same all the way through the book.
 *
 * They age, they are injured, they are disguised, they are remembered as a
 * child in a chapter set thirty years earlier. Until this existed the only ways
 * to say so were editing the cast file by hand and designing a second voice —
 * and the second one silently destroyed the first, because both were stored
 * under the same id.
 *
 * The C# tests cover resolution and the id, and none of them can say whether a
 * writer can reach any of it. So this asserts the two halves that would each
 * leave the feature useless with every unit test still green: the panel offers
 * the actual stretches of this book to choose from, and setting one changes
 * which voice the reading resolves for those lines and only those.
 *
 * Also the Settings section, for the same reason in reverse: preparing an
 * engine used to live behind a button in the one view that shows nothing until
 * you already have one.
 */
test('a voice can be set over one stretch of the book, from the cast rail', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-scoped-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({
    args: ['out/main/index.js', `--user-data-dir=${join(workDir, 'profile')}`],
    env
  })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  const guids = await evaluateWhenReady(
    page,
    async (parent) => {
      const rpc = window.novalistRpc
      let state = await rpc.request('project/create', [parent, 'Scoped', 'Book One'])
      await rpc.request('entities/create', ['character', 'Mira'])

      const write = async (chapterTitle: string, act: string, html: string) => {
        state = await rpc.request('project/createChapter', [chapterTitle])
        const chapters = (state as { chapters: { guid: string }[] }).chapters
        const guid = chapters[chapters.length - 1].guid
        state = await rpc.request('project/setChapterAct', [guid, act])
        state = await rpc.request('project/createScene', [guid, 'Only scene'])
        const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
          .find((c) => c.guid === guid)!.scenes
        const scene = scenes[scenes.length - 1]
        await rpc.request('scenes/write', [guid, scene.id, html, 'text'])
        return guid
      }

      // The same speaker in both acts, so what changes between them is the
      // voice rather than who is talking.
      const early = await write(
        'The harbour wall',
        'One',
        '<p>"You are late," Mira snapped.</p>'
      )
      const late = await write(
        'Thirty years on',
        'Two',
        '<p>"You are late," Mira said.</p>'
      )

      await rpc.request('narration/setVoice', [null, 'narrator-voice'])
      const cast = (await rpc.request('narration/cast')) as {
        members: { characterId: string; name: string }[]
      }
      const mira = cast.members.find((m) => m.name.startsWith('Mira'))!.characterId
      await rpc.request('narration/setVoice', [mira, 'her-usual-voice'])

      window.novalistStores.project.getState().applyState(state as never)
      return { early, late, mira }
    },
    workDir
  )
  await expect(page.locator('.mode-rail')).toBeVisible({ timeout: 30_000 })
  await dismissTour(page)

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('narration'))
  await expect(page.frameLocator('.narration-frame').locator('.nl-chapter')).toHaveCount(2, {
    timeout: 20_000
  })

  // Mira's row, and the button that was not there at all before.
  // The narrator has one of these too - a book with a framing narrator changes
  // teller between its parts - so the row is picked by its name rather than by
  // the first match.
  const miraRow = page
    .locator('.narration-cast-row:not(.narrator)')
    .filter({ has: page.locator('.narration-cast-name', { hasText: 'Mira' }) })
  const scope = miraRow.locator('.narration-scope')
  await expect(scope).toBeVisible()
  // Nothing set yet, so it is not marked. Which characters change voice across
  // the book is a fact about the book and should not need a click to read.
  await expect(scope).not.toHaveClass(/\bset\b/)

  await scope.click()
  const editor = page.locator('.narration-scope-editor')
  await expect(editor).toBeVisible()

  // The stretches this book actually has: each act, each chapter, each scene.
  // A form asking a writer to type a chapter name is a form they can spell
  // wrong, and a scope that matches nothing fails silently.
  const places = editor.locator('select').first()
  const offered = await places.locator('option').allTextContents()
  expect(offered).toContain('One')
  expect(offered).toContain('Two')
  expect(offered).toContain('Thirty years on')
  expect(offered).toContain('Thirty years on — Only scene')

  // Setting it goes through the same store action the picker calls. The picker
  // itself offers only voices this machine has, and a CI runner has none — the
  // thing worth asserting is what happens to the reading, not which options a
  // dropdown drew.
  await page.evaluate(
    async ([mira, late]) => {
      await window.novalistStores.narration
        .getState()
        .setVoiceScope(mira, { act: null, chapter: late, scene: null }, 'her-older-voice')
    },
    [guids.mira, guids.late]
  )

  // The rail says so without being asked again.
  await expect(scope).toHaveClass(/\bset\b/, { timeout: 15_000 })
  await expect(editor.locator('.narration-scope-where')).toHaveText('Thirty years on')

  // And the reading resolves it: her line in the late chapter is read in the
  // scoped voice, her line in the early one is unchanged. This is the assertion
  // the whole feature lives or dies by, and no unit test can make it.
  const heard = await page.evaluate(async () => {
    const book = (await window.novalistRpc.request('narration/book')) as {
      chapters: { title: string; scenes: { segments: { text: string; voiceId: string | null }[] }[] }[]
    }
    return book.chapters.map((chapter) => ({
      title: chapter.title,
      voices: chapter.scenes
        .flatMap((scene) => scene.segments)
        .filter((segment) => segment.text.includes('You are late'))
        .map((segment) => segment.voiceId)
    }))
  })
  expect(heard.find((c) => c.title === 'The harbour wall')?.voices).toEqual(['her-usual-voice'])
  expect(heard.find((c) => c.title === 'Thirty years on')?.voices).toEqual(['her-older-voice'])

  // Clearing it sends those lines back to her standing voice rather than
  // silencing them, which is what an uncast stretch has to mean.
  await page.evaluate(
    async ([mira, late]) => {
      await window.novalistStores.narration
        .getState()
        .setVoiceScope(mira, { act: null, chapter: late, scene: null }, null)
    },
    [guids.mira, guids.late]
  )
  await expect(scope).not.toHaveClass(/\bset\b/, { timeout: 15_000 })

  // Settings knows about speech engines now. Preparing one is an install —
  // gigabytes of model — and it used to be reachable only from a button inside
  // the view that has nothing to show until you have one.
  await page.evaluate(() => window.novalistStores.shell.getState().openSettings('narration'))
  // No engine is installed on a CI runner, and the section says that rather
  // than showing an empty box.
  await expect(page.locator('.settings-hint.export-warning').first()).toBeVisible({
    timeout: 15_000
  })

  await app.close()
})
