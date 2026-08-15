import { test, expect } from '@playwright/test'
import { launchApp, seedBook } from './harness'

async function closeTourIfOffered(page: import('@playwright/test').Page): Promise<void> {
  const tour = page.locator('.tour-card')
  try {
    await tour.waitFor({ state: 'visible', timeout: 1_500 })
    await tour.getByRole('button', { name: 'Close tour' }).click()
  } catch {
    // A migrated profile or a deliberately disabled tour has nothing to close.
  }
}

test('the desktop shell responds to usable width and removes irrelevant chrome', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-responsive-shell-')
  const book = await seedBook(h, { One: ['A'] })
  await closeTourIfOffered(h.page)

  const chapter = book.chapters[0]
  const scene = chapter.scenes[0]
  await h.page.evaluate(
    async ({ chapterGuid, sceneId }) => {
      await window.novalistStores.project.getState().openScene(chapterGuid, sceneId)
    },
    { chapterGuid: chapter.guid, sceneId: scene.id }
  )
  await expect(h.page.locator('.editor-frame')).toBeVisible({ timeout: 30_000 })

  await h.page.setViewportSize({ width: 1400, height: 850 })
  await expect(h.page.locator('.shell')).toHaveAttribute('data-shell-capacity', 'wide')
  await expect(h.page.locator('.binder')).toBeVisible()
  await expect(h.page.locator('.inspector')).toBeVisible()
  await expect(h.page.locator('.toolbar-more')).toHaveCount(0)

  await h.page.setViewportSize({ width: 1080, height: 750 })
  await expect(h.page.locator('.shell')).toHaveAttribute('data-shell-capacity', 'medium')
  await expect(h.page.locator('.binder')).toBeVisible()
  await expect(h.page.locator('.inspector')).toHaveCount(0)
  await expect(h.page.locator('.toolbar-more')).toBeVisible()

  await h.page.setViewportSize({ width: 800, height: 650 })
  await expect(h.page.locator('.shell')).toHaveAttribute('data-shell-capacity', 'compact')
  await expect(h.page.locator('.binder')).toHaveCount(0)
  await expect(h.page.locator('.inspector')).toHaveCount(0)
  await expect(h.page.locator('.toolbar-more')).toBeVisible()

  // Panels become temporary drawers. Opening one must not take width away from
  // the manuscript, and the full action name remains available in the menu.
  await h.page.locator('.toolbar-more > summary').click()
  await h.page.getByRole('button', { name: 'Toggle binder' }).click()
  await expect(h.page.locator('.binder')).toBeVisible()
  const widths = await h.page.evaluate(() => {
    const shell = document.querySelector('.shell') as HTMLElement
    const main = document.querySelector('.shell-main') as HTMLElement
    return {
      shell: shell.getBoundingClientRect().width,
      main: main.getBoundingClientRect().width,
      overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth
    }
  })
  expect(widths.main).toBeGreaterThan(widths.shell * 0.7)
  expect(widths.overflow).toBeLessThanOrEqual(0)

  // Settings is a system task. Project tree, scene inspector, project status,
  // and writing overflow actions are all unrelated and therefore absent.
  await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('appearance'))
  await expect(h.page.locator('.settings-view')).toBeVisible({ timeout: 20_000 })
  await expect(h.page.locator('.binder')).toHaveCount(0)
  await expect(h.page.locator('.inspector')).toHaveCount(0)
  await expect(h.page.locator('.status-bar')).toHaveCount(0)
  await expect(h.page.locator('.toolbar-more')).toHaveCount(0)
  expect(
    await h.page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    )
  ).toBeLessThanOrEqual(0)

  await h.close()
})

test('project status is concise until its details are requested', async () => {
  test.setTimeout(120_000)
  const h = await launchApp('nl-status-overview-')
  await seedBook(h, { One: ['A', 'B'], Two: ['C'] })
  await closeTourIfOffered(h.page)

  const trigger = h.page.locator('.status-overview-trigger')
  await expect(trigger).toBeVisible()
  await expect(trigger).toContainText('Project status')
  await expect(trigger).not.toContainText(' ch')
  await expect(trigger).not.toContainText(' sc')

  await trigger.click()
  const details = h.page.locator('.status-overview-popover')
  await expect(details).toBeVisible()
  // Spelled out rather than abbreviated. They follow a count, so they read
  // lower-case - what matters is that 'ch', 'sc' and 'loc' are gone.
  await expect(details).toContainText('chapters')
  await expect(details).toContainText('scenes')
  await expect(details).toContainText('characters')
  await expect(details).toContainText('locations')

  await h.close()
})

test('the writing toolbar follows the selection and teaches Focus Peek in context', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-writing-context-')
  const book = await seedBook(h, { One: ['A'] })
  await closeTourIfOffered(h.page)
  const chapter = book.chapters[0]
  const scene = chapter.scenes[0]

  await h.rpc('entities/create', ['character', 'Mira'])
  await h.rpc('scenes/write', [
    chapter.guid,
    scene.id,
    '<p>Mira waited by the window.</p>',
    'Mira waited by the window.'
  ])
  await h.page.evaluate(
    async ({ chapterGuid, sceneId }) => {
      await window.novalistStores.project.getState().openScene(chapterGuid, sceneId)
    },
    { chapterGuid: chapter.guid, sceneId: scene.id }
  )

  const editor = h.page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toContainText('Mira waited', { timeout: 30_000 })
  await h.page.waitForTimeout(1_000)

  // A text selection gets character formatting and annotation actions. The
  // former abbreviations are intentionally asserted absent from both toolbars.
  await editor.evaluate((root) => {
    const text = root.firstChild?.firstChild
    if (!text) throw new Error('scene text was not rendered')
    const range = document.createRange()
    range.setStart(text, 5)
    range.setEnd(text, 11)
    const selection = window.getSelection()
    selection?.removeAllRanges()
    selection?.addRange(range)
    document.dispatchEvent(new Event('selectionchange'))
  })
  await expect(h.page.locator('.editor-toolbar')).toHaveAttribute(
    'data-editor-context',
    'selection'
  )
  // Scoped to the toolbar: the inspector's "Footnotes" tab is also a button
  // whose accessible name starts with the same word.
  const toolbar = h.page.locator('.editor-toolbar')
  await expect(toolbar.getByRole('button', { name: 'Comment' })).toBeVisible()
  await expect(toolbar.getByRole('button', { name: 'Footnote', exact: true })).toBeVisible()
  await expect(h.page.locator('.editor-toolbar')).not.toContainText('Cmt')
  await expect(h.page.locator('.editor-toolbar')).not.toContainText('Fn')
  await expect(h.page.getByRole('combobox', { name: 'Paragraph style' })).toHaveCount(0)

  // A collapsed caret inside a known name switches back to paragraph actions,
  // offers the relevant Focus Peek command, and raises a one-time coachmark.
  await editor.evaluate((root) => {
    const text = root.firstChild?.firstChild
    if (!text) throw new Error('scene text was not rendered')
    const range = document.createRange()
    range.setStart(text, 2)
    range.collapse(true)
    const selection = window.getSelection()
    selection?.removeAllRanges()
    selection?.addRange(range)
    document.dispatchEvent(new Event('selectionchange'))
  })
  await expect(h.page.locator('.editor-toolbar')).toHaveAttribute('data-editor-context', 'caret')
  await expect(h.page.getByRole('combobox', { name: 'Paragraph style' })).toBeVisible()
  await expect(h.page.getByRole('button', { name: 'Peek at entity under caret' })).toBeVisible()

  const tip = h.page.locator('[data-onboarding-tip="focus-peek"]')
  await expect(tip).toBeVisible()
  await tip.getByRole('button', { name: 'Try Focus Peek' }).click()
  await expect(h.page.locator('.peek-card')).toBeVisible({ timeout: 10_000 })
  await expect(h.page.locator('.peek-title')).toContainText('Mira')
  await expect(tip).toHaveCount(0)

  // It is persisted as a completed tip, rather than returning with the next
  // selection change on this installation.
  const progress = await h.page.evaluate(() => localStorage.getItem('nl.onboarding'))
  expect(progress).toContain('"focus-peek":"completed"')

  await h.close()
})
