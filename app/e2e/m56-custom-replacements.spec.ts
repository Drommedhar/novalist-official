import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * The writer's own replacement rules, as they type.
 *
 * The rules were always a stored list - the language preset only ever seeded it
 * - but nothing showed the list, so "what gets replaced by what" meant one of
 * eleven presets and nothing else. Storing a rule proves very little on its own:
 * what matters is whether the characters land in the prose, which is why this
 * types into the real editor and reads them back out.
 */

test('a writer can add their own replacement rules, plain and pattern', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-rules-'))
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
    let state = await rpc.request('project/create', [parent, 'Rules', 'Book One'])
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

  // Two rules of the writer's own: one plain, one that captures and puts back.
  await page.evaluate(async () => {
    await window.novalistRpc.request('settings/setAutoReplacements', [
      'global',
      [
        { kind: 'literal', start: '->', end: '->', startReplace: '→', endReplace: '→' },
        { kind: 'regex', start: '(\\d+)x(\\d+)', startReplace: '$1×$2', end: '', endReplace: '' }
      ]
    ])
    await window.novalistStores.settings.getState().load()
  })

  await editor.click()
  await page.keyboard.type('go -> there, 12x9 feet', { delay: 30 })
  await expect
    .poll(async () => editor.innerText(), { timeout: 15_000 })
    .toContain('go → there, 12×9 feet')

  // The rules the preset seeded are gone, because this list replaced them.
  await page.keyboard.type(' and -- nothing', { delay: 30 })
  await expect.poll(async () => editor.innerText(), { timeout: 15_000 }).toContain('and -- nothing')

  // A pattern that cannot run is refused while the writer is still looking at
  // it, rather than stored and silently skipped forever.
  const refused = await page.evaluate(async () => {
    try {
      await window.novalistRpc.request('settings/setAutoReplacements', [
        'global',
        [{ kind: 'regex', start: '(unclosed', startReplace: 'x', end: '', endReplace: '' }]
      ])
      return 'stored'
    } catch {
      return 'refused'
    }
  })
  expect(refused).toBe('refused')

  // Deleting every rule sticks: the preset does not come back on the next read.
  await page.evaluate(async () => {
    await window.novalistRpc.request('settings/setAutoReplacements', ['global', []])
    await window.novalistStores.settings.getState().load()
  })
  const remaining = await page.evaluate(
    () => (window.novalistStores.settings.getState().view!.global.autoReplacements as unknown[]).length
  )
  expect(remaining).toBe(0)

  // ─── The table itself, driven the way a writer drives it ───────────
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('settings' as never))
  await expect(page.locator('#set-theme')).toBeVisible({ timeout: 20_000 })

  const rows = page.locator('.replacement-rule')
  const error = page.locator('.replacement-rules-error')
  await expect(rows).toHaveCount(0)

  // Adding a row must not complain: an empty row is a rule half written, not a
  // rule that was refused.
  await page.getByRole('button', { name: 'Add a rule' }).click()
  await expect(rows).toHaveCount(1)
  await expect(error).toHaveCount(0)

  // It is not stored until it has something to match on.
  expect(
    await page.evaluate(
      () =>
        (window.novalistStores.settings.getState().view!.global.autoReplacements as unknown[]).length
    )
  ).toBe(0)

  await rows.first().getByLabel('When I type').fill('(c)')
  await rows.first().getByLabel('Insert').fill('©')
  await rows.first().getByLabel('Insert').blur()

  await expect
    .poll(async () =>
      page.evaluate(
        () =>
          (window.novalistStores.settings.getState().view!.global.autoReplacements as unknown[])
            .length
      )
    )
    .toBe(1)
  await expect(error).toHaveCount(0)

  await rows.first().getByRole('button', { name: 'Remove this rule' }).click()
  await expect(rows).toHaveCount(0)

  await app.close()
})

/**
 * The preview beside a rule is a port of the editor's own matching, because the
 * editor page is a standalone document with no bundler between them. A preview
 * that disagreed with the editor would be worse than none: the writer would
 * tune a rule against a lie. So this types the same sample into the real editor
 * and requires the two to agree, character for character.
 */
test('the preview beside a rule agrees with what the editor actually types', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-preview-'))
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
    let state = await rpc.request('project/create', [parent, 'Preview', 'Book One'])
    state = await rpc.request('project/createChapter', ['One'])
    const chapters = (state as { chapters: { guid: string }[] }).chapters
    const guid = chapters[chapters.length - 1].guid
    state = await rpc.request('project/createScene', [guid, 'Opening'])
    window.novalistStores.project.getState().applyState(state as never)
  }, workDir)

  // One alternating rule and one pattern with captures: the two shapes whose
  // behaviour is least obvious from reading them.
  await page.evaluate(async () => {
    await window.novalistRpc.request('settings/setAutoReplacements', [
      'global',
      [
        { kind: 'literal', start: "'", end: "'", startReplace: '“', endReplace: '”' },
        { kind: 'regex', start: '(\\d+)x(\\d+)', startReplace: '$1×$2', end: '', endReplace: '' }
      ]
    ])
    await window.novalistStores.settings.getState().load()
  })

  const samples = ["she said 'no' twice", 'the room is 12x9 feet']

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('settings' as never))
  await expect(page.locator('#set-theme')).toBeVisible({ timeout: 20_000 })
  const rows = page.locator('.replacement-rule')
  const predicted: string[] = []
  for (let i = 0; i < samples.length; i++) {
    await rows.nth(i).getByLabel('Try it on').fill(samples[i])
    await expect(rows.nth(i).locator('.rule-tryit-result')).toBeVisible({ timeout: 10_000 })
    predicted.push((await rows.nth(i).locator('.rule-tryit-result').innerText()).trim())
  }
  expect(predicted[0]).toBe('she said “no” twice')
  expect(predicted[1]).toBe('the room is 12×9 feet')

  // Now type the same samples into the real editor, each in its own paragraph
  // so the alternating rule counts from a clean line as the preview does.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await editor.click()

  const typed: string[] = []
  for (const sample of samples) {
    await page.keyboard.press('Enter')
    await page.keyboard.type(sample, { delay: 25 })
    await expect
      .poll(async () => (await editor.innerText()).split('\n').filter(Boolean).length, {
        timeout: 15_000
      })
      .toBeGreaterThan(typed.length)
    const lines = (await editor.innerText()).split('\n').filter((line) => line.trim().length > 0)
    typed.push(lines[lines.length - 1].trim())
  }

  expect(typed).toEqual(predicted)

  await app.close()
})
