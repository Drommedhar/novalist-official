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
  // The overflow is the project bar's own, not a narrow-window fallback: it is
  // the home of the project commands that never had a button.
  await expect(h.page.locator('.toolbar-more')).toBeVisible()

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

  // The overflow no longer carries the panel toggles. Shaping the window is
  // application scope and so the menu bar's; what is down here is the project
  // work that never had a button of its own.
  await h.page.locator('.toolbar-more > summary').click()
  await expect(h.page.getByRole('button', { name: 'Clean up the manuscript' })).toBeVisible()
  await expect(h.page.getByRole('button', { name: 'Toggle binder' })).toHaveCount(0)

  // Panels become temporary drawers, raised by the gesture the View menu names.
  // Opening one must not take width away from the manuscript.
  await h.page.keyboard.press('Control+B')
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

/**
 * Selects `length` characters of the prose, starting `from` characters in.
 *
 * Not by reaching into `firstChild.firstChild`: a name the Codex knows is
 * wrapped in a mention span as soon as the scene loads, so the paragraph's
 * first text node can be four characters long and an offset past it throws.
 */
async function selectInProse(
  editor: import('@playwright/test').Locator,
  from: number,
  length: number
): Promise<void> {
  await editor.evaluate(
    (root, span: { from: number; length: number }) => {
      const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT)
      let seen = 0
      let node = walker.nextNode()
      while (node) {
        const text = node.textContent ?? ''
        if (seen + text.length > span.from) {
          const start = span.from - seen
          const range = document.createRange()
          range.setStart(node, start)
          range.setEnd(node, Math.min(text.length, start + span.length))
          const selection = window.getSelection()
          selection?.removeAllRanges()
          selection?.addRange(range)
          document.dispatchEvent(new Event('selectionchange'))
          return
        }
        seen += text.length
        node = walker.nextNode()
      }
      throw new Error('the prose is shorter than the selection asked for')
    },
    { from, length }
  )
}

test('one command, one home: the writing bar holds still while a selection is made', async () => {
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

  // The writing view's own bar holds what acts on the paragraph the caret is
  // in, and it does not change as the selection does. It used to swap its
  // whole contents on selection, so the row of buttons a writer had just
  // learned moved out from under them the moment they dragged across a word.
  const toolbar = h.page.locator('.editor-toolbar')
  const style = h.page.getByRole('combobox', { name: 'Paragraph style' })
  await expect(style).toBeVisible()
  const beforeSelection = await toolbar.locator('[data-command]').evaluateAll((els) =>
    els.map((el) => el.getAttribute('data-command'))
  )

  await selectInProse(editor, 5, 6)

  const floating = h.page.frameLocator('.editor-frame').locator('#floating-toolbar')
  await expect(floating).toBeVisible({ timeout: 10_000 })
  // Comment and Footnote act on a selection, so the toolbar over the selection
  // is their one home. They used to be here, on the editor toolbar, and in the
  // context menu as well.
  await expect(floating.locator('#ft-comment')).toBeVisible()
  await expect(floating.locator('#ft-footnote')).toBeVisible()
  await expect(toolbar).not.toContainText('Comment')
  await expect(toolbar).not.toContainText('Footnote')
  await expect(toolbar).not.toContainText('Cmt')
  await expect(toolbar).not.toContainText('Fn')
  // Alignment acts on the paragraph, not on what is selected, so it is not up
  // there either - it stays where it always is.
  await expect(floating.locator('#ft-align-left')).toHaveCount(0)

  await expect(style).toBeVisible()
  const afterSelection = await toolbar.locator('[data-command]').evaluateAll((els) =>
    els.map((el) => el.getAttribute('data-command'))
  )
  expect(afterSelection, 'the writing bar changed under the writer').toEqual(beforeSelection)

  // A collapsed caret inside a known name raises the one-time coachmark.
  await selectInProse(editor, 2, 0)
  // Peeking acts on the object under the caret, so its home is the context
  // menu and a gesture - not a button that appears and disappears as the caret
  // moves past names.
  await expect(h.page.getByRole('button', { name: 'Peek at entity under caret' })).toHaveCount(0)

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
