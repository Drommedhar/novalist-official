import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'
import { dismissTour } from './harness'

/**
 * Directing a line by hand, and the audiobook.
 *
 * Both halves are unit-tested to the line in C#, and neither of those suites
 * can say whether a writer can reach any of it. What this asserts is what would
 * make each feature useless while every C# test still passed: the sliders open
 * on what is actually set rather than on zero, pushing one and applying it
 * survives leaving the view, a run of lines can be directed in one go, a
 * character's standing register reaches the lines they speak, and the Audiobook
 * format is reachable in the Export view and says what it will cost before it
 * asks anybody to wait for hours.
 */
test('a line can be directed by hand, and the book rendered as an audiobook', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-hand-'))
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
      let state = await rpc.request('project/create', [parent, 'HandDirection', 'Book One'])
      await rpc.request('entities/create', ['character', 'Mira'])

      state = await rpc.request('project/createChapter', ['The argument'])
      const chapters = (state as { chapters: { guid: string }[] }).chapters
      const guid = chapters[chapters.length - 1].guid
      state = await rpc.request('project/createScene', [guid, 'Kitchen'])
      const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
        .find((c) => c.guid === guid)!.scenes
      const scene = scenes[scenes.length - 1]
      await rpc.request('scenes/write', [
        guid,
        scene.id,
        '<p>"You are late," Mira said.</p><p>"I know," she said.</p><p>"Say it again," she said.</p>',
        'text'
      ])

      window.novalistStores.project.getState().applyState(state as never)
      await window.novalistStores.project.getState().openScene(guid, scene.id)
    },
    workDir
  )
  await expect(page.locator('.mode-rail')).toBeVisible({ timeout: 30_000 })
  await dismissTour(page)

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('narration'))

  const frame = page.frameLocator('.narration-frame')
  const spoken = frame.locator('[data-nl-kind="dialogue"]')
  await expect(spoken).toHaveCount(3, { timeout: 20_000 })

  // ─── the sliders ───

  await spoken.first().click()
  const panel = page.locator('.narration-panel')
  await expect(panel).toBeVisible({ timeout: 10_000 })

  await panel.getByRole('button', { name: /by hand|von hand|手动/i }).click()
  const editor = panel.locator('.narration-direction-editor')
  await expect(editor).toBeVisible()

  // Eight dimensions, and they open on what the line is already directed at
  // rather than on zero - "neutral" is calm 0.6, so the calm slider is not at 0.
  const sliders = editor.locator('.narration-slider input[type="range"]')
  await expect(sliders).toHaveCount(8)
  const values = await editor.locator('.narration-slider-value').allTextContents()
  expect(values.some((v) => Number.parseFloat(v) > 0)).toBe(true)

  // A run rather than one line. The whole scene, which is more than the three
  // spoken lines: the dialogue tags between them are segments too, and a run is
  // a stretch of the reading rather than a count of quotes. The control clamps
  // to what is actually left in the scene.
  const run = panel.locator('input[type="number"]')
  await expect(run).toBeVisible()
  await run.fill('99')

  // Push a dimension the line does not already carry, and apply it.
  const surprised = editor.locator('.narration-slider').filter({ hasText: /surprised|überrascht|惊讶/i })
  await surprised.locator('input[type="range"]').fill('0.75')
  await editor.getByRole('button', { name: /apply|übernehmen|应用/i }).first().click()
  await expect(editor).toBeHidden({ timeout: 10_000 })

  // All three lines are now the writer's, not the prose's - and the numbers
  // survived the round trip through the scene file.
  const directed = await page.evaluate(async () => {
    const book = window.novalistStores.narration.getState().book
    return book?.chapters[0].scenes[0].segments
      .filter((s) => s.kind === 'Dialogue')
      .map((s) => ({ source: s.directionSource, surprised: s.directionVector.surprised }))
  })
  expect(directed).toHaveLength(3)
  for (const line of directed ?? []) {
    expect(line.source).toBe('Writer')
    expect(line.surprised).toBeCloseTo(0.75, 2)
  }

  // ─── a standing register ───

  // By its own label rather than by the row: the narrator's row carries the
  // voice list, which names every voice on the machine, so filtering rows on a
  // character's name can match more than one of them.
  const miraRegister = page.locator('.narration-register[aria-label*="Mira"]')
  await miraRegister.click()
  const register = page.locator('.narration-register-editor')
  await expect(register).toBeVisible()
  const melancholic = register
    .locator('.narration-slider')
    .filter({ hasText: /melancholic|melancholisch|忧郁/i })
  await melancholic.locator('input[type="range"]').fill('0.3')
  await register.getByRole('button', { name: /apply|übernehmen|应用/i }).first().click()
  await expect(register).toBeHidden({ timeout: 10_000 })

  // It reaches the lines she speaks, added to what they already carry - and the
  // whole is still inside what an engine will take. This line was already
  // pushed to 0.75 surprised above, so adding 0.3 melancholic asks for more
  // than 1.5 across the eight dimensions; what arrives is the blend scaled to
  // fit, keeping its proportions, rather than one dimension truncated.
  const performed = await (async () => {
    let last: Record<string, number> = {}
    await expect
      .poll(
        async () => {
          last = await page.evaluate(() => {
            const book = window.novalistStores.narration.getState().book
            const line = book?.chapters[0].scenes[0].segments.find((s) => s.kind === 'Dialogue')
            return line?.directionVector ?? {}
          })
          return last.melancholic ?? 0
        },
        { timeout: 10_000 }
      )
      .toBeGreaterThan(0.2)
    return last
  })()
  const total = Object.values(performed).reduce((sum, value) => sum + value, 0)
  expect(total).toBeLessThanOrEqual(1.501)
  // Scaled together: surprised is still the loudest thing about the line.
  expect(performed.surprised).toBeGreaterThan(performed.melancholic)

  // And it is marked on the rail, so it can be found again.
  await expect(page.locator('.narration-register.set[aria-label*="Mira"]')).toBeVisible()

  // ─── the audiobook ───

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('export'))
  await expect(page.locator('#export-format')).toBeVisible({ timeout: 10_000 })
  await page.selectOption('#export-format', 'Audiobook')

  const audiobook = page.locator('.audiobook')
  await expect(audiobook).toBeVisible({ timeout: 10_000 })

  // What it will cost, before anybody waits hours for it. The word count is
  // real, and the wall clock is honest about being unknown on a machine that
  // has never finished a render.
  const estimate = audiobook.locator('.audiobook-estimate')
  await expect(estimate).toBeVisible({ timeout: 15_000 })
  await expect(estimate).toContainText(/\d/)

  // Three deliveries, and no engine installed - which is said rather than
  // offering a button that would produce silence.
  await expect(audiobook.locator('#audiobook-delivery option')).toHaveCount(3)
  await expect(audiobook.locator('.audiobook-warning')).toBeVisible()

  await app.close()
})
