import { test, expect, _electron as electron, type Page } from '@playwright/test'
import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'
import { readZip, readZipText } from './zip'

/**
 * What comes out of Export, checked by opening the file.
 *
 * The audit marks these rows done against the code that writes them, and the
 * C# side is unit tested at 100% line coverage - which says the exporter does
 * what its author expected, and nothing at all about whether the view hands it
 * the right arguments. That is the failure that has actually shipped here: a
 * Settings switch wired to nothing while the property behind it was fully
 * covered, and a Scrivener reader with twenty green tests that produced an
 * empty book from a real project.
 *
 * So these drive the real export path and then read the artifact. EPUB and
 * DOCX are both zips, read here by a small reader in ./zip rather than by a
 * dependency or by the tar on PATH, which is GNU tar and cannot read zip.
 */

/** One-pixel PNG, enough to be a cover. */
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64'
)

async function launch(workDir: string): Promise<{ app: Awaited<ReturnType<typeof electron.launch>>; page: Page }> {
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })
  return { app, page }
}

/** A book with two scenes, the second of which carries a footnote. */
async function seed(page: Page, parent: string, coverPath: string | null): Promise<void> {
  await evaluateWhenReady(page, async (args: { parent: string; cover: string | null }) => {
    const rpc = window.novalistRpc
    let state = await rpc.request('project/create', [args.parent, 'Artifacts', 'Book One'])
    state = await rpc.request('project/createChapter', ['Chapter One'])
    const chapters = (state as { chapters: { guid: string }[] }).chapters
    const guid = chapters[chapters.length - 1].guid

    state = await rpc.request('project/createScene', [guid, 'Kept scene'])
    state = await rpc.request('project/createScene', [guid, 'Dropped scene'])
    const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
      .find((c) => c.guid === guid)!.scenes

    await rpc.request('scenes/write', [
      guid, scenes[0].id,
      '<p>The harbour was empty<sup class="nv-fn" data-fn-id="f1">1</sup>.</p>',
      'The harbour was empty.'
    ])
    await rpc.request('scenes/write', [
      guid, scenes[1].id, '<p>UNIQUEDROPPEDPROSE marker.</p>', 'UNIQUEDROPPEDPROSE marker.'
    ])
    // Held back from the compile, the way the binder's Exclude scene does it.
    await rpc.request('sceneBulk/setExportInclusion', [[scenes[1].id], false])
    if (args.cover) await rpc.request('dashboard/setCover', [args.cover])

    window.novalistStores.project.getState().applyState(
      (await rpc.request('project/getState')) as never
    )
  }, { parent, cover: coverPath })
}

async function runExport(page: Page, format: string, outputPath: string): Promise<unknown> {
  return page.evaluate(async (args: { format: string; out: string }) => {
    const rpc = window.novalistRpc
    const state = window.novalistStores.project.getState()
    const guids = (state.chapters as { guid: string }[]).map((c) => c.guid)
    return rpc.request('export/run', [
      args.format, args.out, 'Artifacts', 'A Writer', true, guids
    ])
  }, { format, out: outputPath })
}

test('the cover reaches the EPUB as a real cover, not just a file in the zip', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-exp-cover-'))
  const cover = join(workDir, 'cover.png')
  writeFileSync(cover, PNG)

  const { app, page } = await launch(workDir)
  await seed(page, workDir, cover)

  const out = join(workDir, 'book.epub')
  await runExport(page, 'Epub', out)

  const text = readZipText(out)

  // A reader shows the cover from the manifest entry, not from a stray image.
  expect(text).toContain('properties="cover-image"')
  // EPUB 2's meta, which several retailers still read in preference.
  expect(text).toMatch(/<meta[^>]+name="cover"/)

  await app.close()
})

test('a scene excluded from the compile is absent from every format', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-exp-exclude-'))
  const { app, page } = await launch(workDir)
  await seed(page, workDir, null)

  for (const [format, ext] of [['Epub', 'epub'], ['Markdown', 'md']] as const) {
    const out = join(workDir, `book.${ext}`)
    await runExport(page, format, out)

    const body = ext === 'md' ? readFileSync(out, 'utf8') : readZipText(out)

    expect(body, `${format} kept the excluded scene`).not.toContain('UNIQUEDROPPEDPROSE')
    expect(body, `${format} lost the kept scene`).toContain('The harbour was empty')
  }

  await app.close()
})

test('a footnote exports as a real note, not as literal text', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-exp-fn-'))
  const { app, page } = await launch(workDir)

  await seed(page, workDir, null)
  // The note itself lives beside the prose, keyed by the anchor's id.
  await page.evaluate(async () => {
    const rpc = window.novalistRpc
    const state = window.novalistStores.project.getState()
    const chapter = (state.chapters as { guid: string; scenes: { id: string }[] }[])[0]
    await rpc.request('scenes/setAnnotations', [
      chapter.guid,
      chapter.scenes[0].id,
      [],
      [{ id: 'f1', number: 1, text: 'Emptied by the tide, not by the war.' }]
    ])
  })

  const out = join(workDir, 'book.docx')
  await runExport(page, 'Docx', out)

  const parts = readZip(out)

  // Word's own footnote part: without it the note is prose in the body.
  expect([...parts.keys()], 'no word/footnotes.xml in the DOCX').toContain('word/footnotes.xml')
  expect(parts.get('word/footnotes.xml')!.toString('utf8')).toContain('Emptied by the tide')

  await app.close()
})
