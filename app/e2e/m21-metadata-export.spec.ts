import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync, readFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * The book as data, reachable from the Export view.
 *
 * Every writer in DataExport.cs - the CSV quoting, the scene sheet, the JSON
 * document - was built, and nothing ever filled a MetadataExport to hand them.
 * The whole feature existed and no screen could produce a single file from it.
 */
test('the export view writes the scene sheet, the codex sheet and the json', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-meta-'))
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
    let state = await rpc.request('project/create', [parent, 'Outline', 'Book One'])
    state = await rpc.request('project/createChapter', ['Chapter One'])
    const chapters = (state as { chapters: { guid: string }[] }).chapters
    const guid = chapters[chapters.length - 1].guid
    state = await rpc.request('project/createScene', [guid, 'The gate'])
    window.novalistStores.project.getState().applyState(state as never)
    await rpc.request('entities/create', ['character', 'Mira'])
  }, workDir)
  await expect(page.locator('.activity-bar')).toBeVisible({ timeout: 30_000 })

  // The save dialog is native, so the path is fixed for the run rather than
  // clicked through - everything else goes the way a writer would.
  const scenes = join(workDir, 'scenes.csv')
  const codex = join(workDir, 'codex.csv')
  const everything = join(workDir, 'all.json')

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('export'))
  await expect(page.locator('.export-metadata-actions')).toBeAttached({ timeout: 20_000 })

  for (const [path, kind] of [
    [scenes, 'sceneCsv'],
    [codex, 'codexCsv'],
    [everything, 'json']
  ] as const) {
    const ok = await page.evaluate(
      ([out, format]) =>
        window.novalistRpc
          .request<{ success: boolean }>('export/metadata', [out, format])
          .then((r) => r.success),
      [path, kind] as const
    )
    expect(ok).toBe(true)
  }

  // Excel needs the byte order mark to read UTF-8; the header is checked past it.
  const header = (text: string): string => text.replace(/^﻿/, '').split(/\r?\n/)[0]

  const sceneSheet = readFileSync(scenes, 'utf8')
  expect(sceneSheet.startsWith('﻿')).toBe(true)
  expect(header(sceneSheet)).toContain('Chapter,Chapter order,Scene')
  expect(sceneSheet).toContain('The gate')

  // One row per field, so an entry with its own shape still fits the sheet.
  const codexSheet = readFileSync(codex, 'utf8')
  expect(header(codexSheet)).toBe('Kind,Name,Field,Value')

  // And no mark on the JSON, which a strict parser would refuse.
  const jsonText = readFileSync(everything, 'utf8')
  expect(jsonText.startsWith('﻿')).toBe(false)
  const parsed = JSON.parse(jsonText) as {
    title: string
    scenes: unknown[]
    codex: { name: string }[]
  }
  expect(parsed.title).toBe('Book One')
  expect(parsed.scenes).toHaveLength(1)
  expect(parsed.codex.some((e) => e.name === 'Mira')).toBe(true)

  await app.close()
})
