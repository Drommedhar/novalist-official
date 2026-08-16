import { test, expect } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { dismissTour, enterWriting, launchApp } from './harness'

/**
 * A whole Scrivener project, imported and then opened.
 *
 * Import used to be checked a piece at a time - a paragraph of RTF, a binder
 * with three documents - which says nothing about what a writer gets when they
 * point Novalist at a real project. This runs the sample end to end and then
 * does the thing they do next: open the biggest scene and type in it.
 *
 * Set NOVALIST_SCRIVENER_SAMPLE to a .scriv folder to run it; skipped without.
 */
const SAMPLE =
  process.env.NOVALIST_SCRIVENER_SAMPLE
  ?? 'C:/Users/domin/Downloads/scriv_proj/SampleProject.scriv'

test('a Scrivener project imports whole, and its scenes open ready to write in', async () => {
  test.skip(!existsSync(SAMPLE), 'no Scrivener sample available')
  test.setTimeout(400_000)
  const h = await launchApp('nl-scriv-')
  const page = h.page
  await dismissTour(page)

  const dir = mkdtempSync(join(tmpdir(), 'nl-scriv-into-'))
  await page.evaluate(async (d) => {
    const st = await window.novalistRpc.request('project/create', [d, 'Imported', 'Book', 'blank'])
    window.novalistStores.project.getState().applyState(st as never)
  }, dir)

  const result = (await page.evaluate(
    async (src) => window.novalistRpc.request('manuscriptImport/run', [src]),
    SAMPLE
  )) as { chapters: number; scenes: number; words: number; characters: number; research: number }

  // The binder, the Codex entries and the research all come across - not just
  // the prose, which is what import used to be.
  expect(result.chapters).toBeGreaterThan(0)
  expect(result.scenes).toBeGreaterThan(0)
  expect(result.words).toBeGreaterThan(1000)
  expect(result.characters).toBeGreaterThan(0)

  await page.evaluate(async () => {
    const st = await window.novalistRpc.request('project/getState', [])
    window.novalistStores.project.getState().applyState(st as never)
  })

  const scenes = await page.evaluate(async () => {
    const st = window.novalistStores.project.getState()
    const out: { title: string; html: string }[] = []
    for (const ch of st.chapters) {
      for (const sc of ch.scenes) {
        const raw = (await window.novalistRpc.request('scenes/read', [ch.guid, sc.id])) as {
          html: string
        }
        out.push({ title: sc.title, html: raw.html ?? '' })
      }
    }
    return out
  })
  expect(scenes.length).toBe(result.scenes)

  // Formatting that carries meaning survives; Scrivener's own internal markup
  // does not, and neither do the mojibake sequences a mis-decoded RTF leaves
  // behind where its punctuation should be.
  const all = scenes.map((s) => s.html).join('')
  expect(all).toContain('<p>')
  expect(all).toMatch(/font-weight:\s*bold|<(b|strong)[ >]/)
  expect(all).not.toContain("'93")
  expect(all).not.toContain("'94")
  expect(all).not.toMatch(/\[a-z]+\d*\b/) // stray RTF control words in the prose

  // The list in the sample's front matter is a real list in the source, so it
  // has to arrive as one rather than as a run of paragraphs.
  expect(all).toContain('<li>')

  // And then the part a writer actually does. The scenes here are thousands of
  // words each, which is where an import that produced bloated markup would
  // show up as an editor that takes an age to open.
  await enterWriting(page)
  const started = Date.now()
  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).not.toBeEmpty({ timeout: 180_000 })
  await editor.click()
  await page.keyboard.type('XYZ')
  await expect(editor).toContainText('XYZ', { timeout: 180_000 })
  expect(Date.now() - started).toBeLessThan(60_000)

  await h.close()
})
