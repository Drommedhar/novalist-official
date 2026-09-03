import { test, expect, type FrameLocator } from '@playwright/test'
import { enterWriting, launchApp, seedBook } from './harness'

const PROSE = [
  '<p id="ordinary">Ordinary prose.</p>',
  '<div id="ordinaryDiv">Pasted div prose.</div>',
  '<p id="heading" class="nv-style-heading">Heading</p>',
  '<p id="subheading" class="nv-style-subheading">Subheading</p>',
  '<p id="quote" class="nv-style-blockquote">Quoted prose.</p>',
  '<p id="poetry" class="nv-style-poetry">Verse.</p>',
  '<ol><li><p id="list">List item.</p></li></ol>',
  '<p id="image" class="nv-image">Image.</p>',
  '<p id="centred" style="text-align:center">Centred.</p>',
  '<p id="right" style="text-align: right">Right.</p>',
  '<p id="break">***</p>',
  '<p id="breakStars">* * * *</p>',
  '<p id="breakHash">#</p>',
  '<p id="breakUnderscore">_ _ _</p>',
  '<p id="breakBullet">• • •</p>',
  '<p id="breakTilde">~ ~ ~</p>',
  '<p id="breakMixed">- * ~</p>',
  '<p id="breakNbsp">&nbsp;*&nbsp;*&nbsp;*&nbsp;</p>'
].join('')

type Indents = Record<string, number>

async function measuredIndents(
  frame: FrameLocator,
  root: string
): Promise<Indents> {
  return frame.locator(root).evaluate((editor) => {
    const ids = [
      'ordinary',
      'ordinaryDiv',
      'heading',
      'subheading',
      'quote',
      'poetry',
      'list',
      'image',
      'centred',
      'right',
      'break',
      'breakStars',
      'breakHash',
      'breakUnderscore',
      'breakBullet',
      'breakTilde',
      'breakMixed',
      'breakNbsp'
    ]
    return Object.fromEntries(
      ids.map((id) => {
        const paragraph = editor.querySelector<HTMLElement>(`#${id}`)!
        return [id, Number.parseFloat(getComputedStyle(paragraph).textIndent)]
      })
    )
  })
}

function expectOnlyOrdinaryIndented(indents: Indents): void {
  expect(indents.ordinary).toBeGreaterThan(0)
  expect(indents.ordinaryDiv).toBeGreaterThan(0)
  for (const [kind, indent] of Object.entries(indents)) {
    if (kind !== 'ordinary' && kind !== 'ordinaryDiv') expect(indent, kind).toBe(0)
  }
}

test('first-line indent is configurable and reaches every prose view', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-first-line-indent-')
  const book = await seedBook(h, { One: ['A'] })
  const chapter = book.chapters[0]
  const scene = chapter.scenes[0]

  await h.rpc('scenes/write', [chapter.guid, scene.id, PROSE, 'Ordinary prose.'])

  await h.page.evaluate(() =>
    window.novalistStores.shell.getState().openSettings('settings/editor/first-line-indent')
  )
  const input = h.page.locator('#set-first-line-indent')
  await expect(input).toBeFocused()
  await expect(input).toHaveValue('0')
  await input.fill('2')
  await input.blur()
  await expect
    .poll(() =>
      h.page.evaluate(
        () => window.novalistStores.settings.getState().view?.effective.editorFirstLineIndent
      )
    )
    .toBe(2)

  await enterWriting(h.page)
  await h.page.locator('.binder-scene-row').click()
  const sceneFrame = h.page.frameLocator('.editor-frame')
  await expect(sceneFrame.locator('#ordinary')).toBeVisible({ timeout: 30_000 })
  const initialSceneIndent = await measuredIndents(sceneFrame, '#editor')
  expectOnlyOrdinaryIndented(initialSceneIndent)

  await h.page.evaluate(() =>
    window.novalistStores.settings.getState().update('global', { editorFirstLineIndent: 1 })
  )
  await expect
    .poll(async () => (await measuredIndents(sceneFrame, '#editor')).ordinary)
    .toBeLessThan(initialSceneIndent.ordinary)

  await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
  await expect(h.page.locator('iframe[title="manuscript"]')).toBeVisible({ timeout: 30_000 })
  const manuscriptFrame = h.page.frameLocator('iframe[title="manuscript"]')
  await expect(manuscriptFrame.locator('.scene-editor')).toHaveCount(1, { timeout: 30_000 })
  await expect(manuscriptFrame.locator('#ordinary')).toBeVisible({ timeout: 30_000 })
  const initialManuscriptIndent = await measuredIndents(manuscriptFrame, '.scene-editor')
  expectOnlyOrdinaryIndented(initialManuscriptIndent)

  await h.page.evaluate(() =>
    window.novalistStores.settings.getState().update('global', { editorFirstLineIndent: 1.5 })
  )
  await expect
    .poll(async () => (await measuredIndents(manuscriptFrame, '.scene-editor')).ordinary)
    .toBeGreaterThan(initialManuscriptIndent.ordinary)

  await h.rpc('expose/save', [PROSE])
  await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('expose'))
  const exposeFrame = h.page.frameLocator('.expose-editor .editor-frame')
  const expose = exposeFrame.locator('#editor')
  await expect(expose).toBeVisible({ timeout: 30_000 })
  await expect(exposeFrame.locator('#ordinary')).toBeVisible({ timeout: 30_000 })
  const initialExposeIndent = await measuredIndents(exposeFrame, '#editor')
  expectOnlyOrdinaryIndented(initialExposeIndent)

  await h.page.evaluate(() =>
    window.novalistStores.settings.getState().update('global', { editorFirstLineIndent: 0.5 })
  )
  await expect
    .poll(async () => (await measuredIndents(exposeFrame, '#editor')).ordinary)
    .toBeLessThan(initialExposeIndent.ordinary)

  await h.close()
})
