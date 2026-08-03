import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'
import { REAL_PROJECT } from './realProject'

/**
 * Regression: a grammar result arriving seconds after the request must not move
 * the caret. The bug saved the caret as a flat character offset over the whole
 * editor, but Range.toString() puts no separator between blocks — so "end of
 * paragraph N", "an empty paragraph after N" and "column 1 of paragraph N+1"
 * all collapsed onto the same number, and the restore resolved it to the
 * earliest one. Clicking or arrowing onto an empty line or to the start of a
 * line therefore snapped the caret back to the end of the previous paragraph
 * once the LanguageTool round trip returned.
 */

const FIXTURE =
  '<p id="nlt-a">The quick brown fox jumps over the lazy dog.</p>' +
  '<p id="nlt-b"><br></p>' +
  '<p id="nlt-c">Second visible paragraph after the empty line.</p>'

// Offsets into the first paragraph, so the issue is real work but never
// overlaps the paragraphs the caret is parked in.
const ISSUES = JSON.stringify([
  { offset: 4, length: 5, type: 'grammar', message: 'Regression fixture issue' }
])

test('editor: a late grammar result keeps the caret on its own line', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-caret-'))
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

  await page.locator('.binder-scene-row').first().click()
  const editor = page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toBeVisible({ timeout: 30_000 })
  await expect.poll(async () => (await editor.innerText()).trim().length, { timeout: 15_000 }).toBeGreaterThan(20)

  // Park the caret on a line, deliver a grammar result, report where it ended
  // up as "<block id>:<offset within that block>".
  const caretAfterGrammarResult = async (blockId: string, offset: number) =>
    page.evaluate(
      ({ blockId, offset, fixture, issues }) => {
        const frame = document.querySelector('.editor-frame') as HTMLIFrameElement
        const w = frame.contentWindow as unknown as {
          setContent(html: string): void
          setGrammarIssues(json: string): void
          document: Document
          getSelection(): Selection | null
        }
        w.setContent(fixture)

        const doc = w.document
        const root = doc.getElementById('editor') as HTMLElement
        root.focus()

        const block = doc.getElementById(blockId) as HTMLElement
        const first = block.firstChild
        const range = doc.createRange()
        if (first && first.nodeType === Node.TEXT_NODE) range.setStart(first, offset)
        else range.setStart(block, 0)
        range.collapse(true)
        const sel = w.getSelection() as Selection
        sel.removeAllRanges()
        sel.addRange(range)

        // The late arrival the user never asked for.
        w.setGrammarIssues(issues)

        const after = (w.getSelection() as Selection).getRangeAt(0)
        let el =
          after.startContainer.nodeType === Node.TEXT_NODE
            ? after.startContainer.parentElement
            : (after.startContainer as HTMLElement)
        while (el && el !== root && !el.id.startsWith('nlt-')) el = el.parentElement
        const pre = doc.createRange()
        pre.selectNodeContents(el as HTMLElement)
        pre.setEnd(after.startContainer, after.startOffset)
        return `${el === root ? 'editor' : (el as HTMLElement).id}:${pre.toString().length}`
      },
      { blockId, offset, fixture: FIXTURE, issues: ISSUES }
    )

  // The empty line: used to jump to the end of the paragraph above it.
  expect(await caretAfterGrammarResult('nlt-b', 0)).toBe('nlt-b:0')

  // Column 1 of a non-empty line: same collapse, same jump.
  expect(await caretAfterGrammarResult('nlt-c', 0)).toBe('nlt-c:0')

  // Positions that were already unambiguous must stay put too.
  expect(await caretAfterGrammarResult('nlt-c', 7)).toBe('nlt-c:7')
  expect(await caretAfterGrammarResult('nlt-a', 43)).toBe('nlt-a:43')

  await app.close()
})
