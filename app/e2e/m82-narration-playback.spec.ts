import { test, expect, _electron as electron } from '@playwright/test'
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'
import { dismissTour } from './harness'

/**
 * Pressing Play actually produces sound.
 *
 * Every piece of this was tested and the feature was silent, so this asserts
 * the one thing no unit test can: that after pressing Play a clip is rendered,
 * survives in the cache, and is spoken.
 *
 * What it deliberately does not claim to catch is the race that caused the
 * silence - Play stopping the render it had just started, which the backend log
 * reported as "clips=1 ... cacheBytes=0". That needs an engine slow enough for
 * the cancel to land mid-render, and the engine here is a tone generator that
 * finishes instantly; the test passes either way. The fix for that is ordering
 * in the store, and this stands guard over the path around it.
 */
test('pressing Play renders a clip, keeps it, and speaks it', async () => {
  test.setTimeout(180_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-playback-'))
  const settingsDir = join(workDir, 'settings')
  const extensionDir = join(settingsDir, 'Extensions', 'Toolkit')
  mkdirSync(extensionDir, { recursive: true })

  const candidates = [
    join(process.cwd(), '..', 'Novalist.Sdk.Example', 'bin', 'Debug', 'net8.0', 'Novalist.Sdk.Example.dll'),
    join(process.cwd(), 'out', 'backend', 'Novalist.Sdk.Example.dll')
  ]
  const assembly = candidates.find((c) => existsSync(c)) ?? candidates[0]
  test.skip(!existsSync(assembly), 'sample extension assembly not built')
  copyFileSync(assembly, join(extensionDir, 'Novalist.Sdk.Example.dll'))
  writeFileSync(
    join(extensionDir, 'extension.json'),
    JSON.stringify({
      id: 'com.novalist.writingtoolkit',
      name: 'Writing Toolkit',
      version: '1.0.0',
      entryAssembly: 'Novalist.Sdk.Example.dll'
    })
  )

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = settingsDir
  env.NOVALIST_NO_SPLASH = '1'

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
      let state = await rpc.request('project/create', [parent, 'Playback', 'Book One'])
      state = await rpc.request('project/createChapter', ['One'])
      const chapters = (state as { chapters: { guid: string }[] }).chapters
      const guid = chapters[chapters.length - 1].guid
      state = await rpc.request('project/createScene', [guid, 'S'])
      const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
        .find((c) => c.guid === guid)!.scenes
      await rpc.request('scenes/write', [
        guid,
        scenes[scenes.length - 1].id,
        '<p>The harbour was empty. The tide turned.</p>',
        'text'
      ])
      window.novalistStores.project.getState().applyState(state as never)
    },
    workDir
  )
  await expect(page.locator('.mode-rail')).toBeVisible({ timeout: 30_000 })
  await dismissTour(page)

  // Extensions load when the writer opens the Extensions view.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('extensions'))
  await page.waitForTimeout(3000)

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('narration'))

  // Prepare the sample engine, then give the narrator a voice - the whole scene
  // is narration, so the narrator is all this reading needs.
  const prepare = page.locator('.narration-prepare')
  await expect(prepare).toBeVisible({ timeout: 20_000 })
  await prepare.click()

  const narratorDesign = page.locator('.narration-cast-row.narrator .narration-design')
  await expect(narratorDesign).toBeVisible({ timeout: 20_000 })
  await narratorDesign.click()
  const dialog = page.locator('.narration-design-dialog')
  await expect(dialog).toBeVisible({ timeout: 15_000 })
  await dialog.locator('.narration-play').click()
  await expect(dialog).toHaveCount(0, { timeout: 60_000 })

  // Press Play the way a writer does.
  await page.locator('.narration-play').first().click()

  // A clip is spoken - the reading gets past its first line, which is what
  // total breakage of the playback path would take away.
  await expect
    .poll(
      async () => await page.evaluate(() => window.novalistStores.narration.getState().speaking),
      { timeout: 60_000, message: 'nothing was ever spoken' }
    )
    .not.toBeNull()

  // And the clip it is playing is still there to be played - the cache was not
  // emptied under the reading.
  const audible = await page.evaluate(async () => {
    const render = (await window.novalistRpc.request('narration/render', [0, 2, 1.0])) as {
      clips: { clip: string | null }[]
    }
    const name = render.clips.find((c) => c.clip !== null)?.clip
    if (!name) return { clips: render.clips.length, bytes: 0 }
    const response = await fetch(`novalist-audio://clip/${name}`)
    return { clips: render.clips.length, bytes: (await response.arrayBuffer()).byteLength }
  })

  expect(audible.clips).toBeGreaterThan(0)
  expect(audible.bytes).toBeGreaterThan(0)

  await page.evaluate(() => window.novalistStores.narration.getState().stop())
  await app.close()
})
