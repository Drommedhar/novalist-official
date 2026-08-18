import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'
import { dismissTour } from './harness'

/**
 * The book as it will be read aloud.
 *
 * Every piece of this is unit-tested and none of those tests can say whether a
 * writer can reach it: the prose is marked up in Core, cast in one RPC and
 * directed in another, and the frame is the only thing that puts the three
 * together. So this asserts what would each make the feature useless while
 * every C# test still passed - the whole book is there rather than one scene,
 * the writer's own paragraphs survive being marked up, a spoken line is tinted
 * with its speaker and carries its direction, clicking one opens its controls,
 * a speaker corrected here is still corrected after leaving the view, and Play
 * is not offered before there is a voice to play it with.
 */
test('the narration view reads the whole book as prose, cast and directed', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-narration-'))
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

  await evaluateWhenReady(
    page,
    async (parent) => {
      const rpc = window.novalistRpc
      let state = await rpc.request('project/create', [parent, 'Narration', 'Book One'])
      await rpc.request('entities/create', ['character', 'Mira'])
      await rpc.request('entities/create', ['character', 'Aldric'])

      const write = async (chapterTitle: string, sceneTitle: string, html: string) => {
        state = await rpc.request('project/createChapter', [chapterTitle])
        const chapters = (state as { chapters: { guid: string }[] }).chapters
        const guid = chapters[chapters.length - 1].guid
        state = await rpc.request('project/createScene', [guid, sceneTitle])
        const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
          .find((c) => c.guid === guid)!.scenes
        const scene = scenes[scenes.length - 1]
        await rpc.request('scenes/write', [guid, scene.id, html, 'text'])
        return { guid, sceneId: scene.id }
      }

      // Narration, then a line the prose tags with a speech verb, then one it
      // tags plainly, then one it does not tag at all - and emphasis inside the
      // prose, because marking a reading up must not destroy the writer's
      // formatting.
      const first = await write(
        'The harbour wall',
        'Low tide',
        '<p>She had been on the wall since the tide <em>turned</em>. ' +
          '"You are late," Mira snapped.</p>' +
          '<p>"The road was out at Kessel," said Aldric.</p>' +
          '<p>"Then you should have come by water."</p>'
      )
      // A second chapter, which the view only shows if it reads the book rather
      // than the open scene. Its first line names nobody and follows nobody, so
      // it is the one attribution genuinely cannot place.
      await write(
        'Kessel',
        'The road',
        '<p>"Who is there?"</p><p>The road was still out in the morning.</p>'
      )

      window.novalistStores.project.getState().applyState(state as never)
      await window.novalistStores.project.getState().openScene(first.guid, first.sceneId)
    },
    workDir
  )
  await expect(page.locator('.mode-rail')).toBeVisible({ timeout: 30_000 })
  await dismissTour(page)

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('narration'))

  const frame = page.frameLocator('.narration-frame')

  // The whole book, not the open scene. This is the assertion the earlier cut
  // could not make: it showed whichever scene the editor had, and the only way
  // to change that was the binder, which puts the editor back in the pane.
  await expect(frame.locator('.nl-chapter')).toHaveCount(2, { timeout: 20_000 })
  await expect(frame.locator('.nl-chapter-title').first()).toHaveText('The harbour wall')
  await expect(frame.locator('.nl-chapter-title').nth(1)).toHaveText('Kessel')

  // The writer's own prose, paragraphs and emphasis intact, with the reading
  // marked up inside it rather than extracted out of it.
  const prose = frame.locator('.nl-scene[data-scene-id] .nl-prose').first()
  await expect(prose.locator('p')).toHaveCount(3)
  await expect(prose.locator('em')).toHaveText('turned')
  await expect(prose).toContainText('She had been on the wall since the tide turned.')
  await expect(prose).toContainText('Then you should have come by water.')

  // The spoken lines, each marked as dialogue where it stands.
  const spoken = frame.locator('[data-nl-kind="dialogue"]')
  await expect(spoken).toHaveCount(4)
  await expect(spoken.first()).toHaveText('"You are late,"')
  // The tag is the narrator's: it is marked, and not as dialogue. Note the
  // emphasis above cuts its own narration run into three markers - a marker
  // never contains a tag - so this asks for the one holding the tag rather than
  // for a position.
  await expect(
    prose.locator('[data-nl-kind="narration"]').filter({ hasText: 'Mira snapped.' })
  ).toHaveCount(1)

  // The line nobody could be found for is called out where it stands, so it is
  // findable by scrolling rather than by opening every line in turn. Only the
  // second chapter's opener qualifies: the untagged line in the first follows
  // two speakers, so alternation places it - as a guess, which is a different
  // thing from nobody.
  await expect(frame.locator('[data-nl-unknown="1"]')).toHaveCount(1)
  await expect(frame.locator('[data-nl-unknown="1"]')).toHaveText('"Who is there?"')

  // Nothing is cast, so there is no reading to start - and the transport says
  // that rather than offering a button that does nothing.
  const play = page.locator('.narration-play')
  await expect(play).toBeDisabled()
  await expect(page.locator('.narration-transport-note')).toBeVisible()

  // Clicking a line opens its controls, and says how it was directed and why.
  await spoken.first().click()
  const panel = page.locator('.narration-panel')
  await expect(panel).toBeVisible({ timeout: 10_000 })
  await expect(panel.locator('.narration-panel-where')).toContainText('The harbour wall')
  await expect(panel.locator('.narration-chip.source.verb')).toHaveText(/snapped/)
  await expect(panel.locator('.narration-panel-field select').nth(1)).toHaveValue('angry')

  // Casting the narrator alone is enough to read the whole book, because every
  // uncast character falls back to them. The voice id is written directly: a CI
  // runner has no system voices to pick from, and a cast naming a voice this
  // machine does not have is exactly the case that has to work.
  await page.evaluate(() => window.novalistRpc.request('narration/setVoice', [null, 'test-voice']))
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('narration'))
  await expect(page.locator('.narration-play')).toBeEnabled({ timeout: 20_000 })

  // The untagged line in the first chapter is a guess, and the view offers who
  // else it might be. Taking the suggestion is the correction this view exists
  // to make possible without leaving the reading.
  await spoken.nth(2).click()
  await expect(page.locator('.narration-panel .narration-chip.confidence.low')).toBeVisible({
    timeout: 15_000
  })
  await page.locator('.narration-panel .narration-candidate').first().click()

  // It survives leaving the view: the override is stored against the scene, not
  // held in the store the view happens to be using.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('narration'))
  const reloaded = page.frameLocator('.narration-frame')
  await expect(reloaded.locator('[data-nl-kind="dialogue"]')).toHaveCount(4, { timeout: 20_000 })
  await reloaded.locator('[data-nl-kind="dialogue"]').nth(2).click()
  await expect(page.locator('.narration-panel .narration-chip.confidence.manual')).toBeVisible({
    timeout: 15_000
  })

  // And it is the same correction the Dialogue view stores. One override, two
  // views - the whole reason narration does not carry an attribution of its own.
  const fromDialogue = await page.evaluate(async () => {
    const index = (await window.novalistRpc.request('dialogue/index', [null])) as {
      groups: { scenes: { lines: { text: string; confidence: string }[] }[] }[]
    }
    return index.groups
      .flatMap((g) => g.scenes)
      .flatMap((s) => s.lines)
      .filter((l) => l.confidence === 'Manual')
      .map((l) => l.text)
  })
  expect(fromDialogue).toContain('Then you should have come by water.')

  await app.close()
})
