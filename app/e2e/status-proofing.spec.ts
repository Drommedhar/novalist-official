import { expect, test, type Locator, type Page } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'

interface GrammarRequest {
  type: 'grammarCheckRequest'
  requestId: number
  plainText: string
}

interface FixtureIssue {
  offset: number
  length: number
  type: 'grammar' | 'spelling' | 'style'
  message: string
  replacements: string[]
}

interface ProofingFixtureWindow extends EditorWindow {
  proofingRequests: GrammarRequest[]
  invokeCSharpAction(json: string): void
  requestGrammarCheck(): void
}

async function installCheckerFixture(page: Page): Promise<void> {
  // Supply only checker replies. Ready, focus, status and editor messages still
  // travel through the real iframe bridge, with no external grammar endpoint.
  await page.addInitScript(() => {
    if (window.parent === window) return
    const editor = window as unknown as ProofingFixtureWindow
    editor.proofingRequests = []
    editor.invokeCSharpAction = (json: string): void => {
      const message = JSON.parse(json)
      if (message.type === 'grammarCheckRequest') editor.proofingRequests.push(message)
      else window.parent.postMessage({ novalistEditor: json }, '*')
    }
  })
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.evaluate(() => window.novalistStores.settings.getState().update('global', {
    grammarCheckEnabled: true,
    autoReplacementEnabled: false
  }))
}

async function newestRequest(editor: Locator): Promise<GrammarRequest> {
  await expect.poll(() => editor.evaluate(element =>
    (element.ownerDocument.defaultView as unknown as ProofingFixtureWindow).proofingRequests.length
  )).toBeGreaterThan(0)
  return editor.evaluate(element =>
    (element.ownerDocument.defaultView as unknown as ProofingFixtureWindow).proofingRequests.at(-1)!
  )
}

async function requestCheck(editor: Locator): Promise<GrammarRequest> {
  const previous = await editor.evaluate(element => {
    const frame = element.ownerDocument.defaultView as unknown as ProofingFixtureWindow
    const count = frame.proofingRequests.length
    frame.requestGrammarCheck()
    return count
  })
  await expect.poll(() => editor.evaluate(element =>
    (element.ownerDocument.defaultView as unknown as ProofingFixtureWindow).proofingRequests.length
  )).toBeGreaterThan(previous)
  return newestRequest(editor)
}

async function respond(editor: Locator, issues: FixtureIssue[], requestId?: number): Promise<void> {
  const id = requestId ?? (await newestRequest(editor)).requestId
  await editor.evaluate((element, response) => {
    const frame = element.ownerDocument.defaultView as unknown as ProofingFixtureWindow
    frame.setGrammarIssues(JSON.stringify(response.issues), response.id)
  }, { issues, id })
}

function issue(offset: number, length: number, type: FixtureIssue['type'], message: string): FixtureIssue {
  return { offset, length, type, message, replacements: ['correction'] }
}

test('desktop proofing lives in the status bar and opens the requested kind of issue', async () => {
  const h = await launchApp('nl-status-proofing-')
  try {
    await installCheckerFixture(h.page)
    const book = await seedBook(h, { Chapter: ['Proofing scene'] })
    await dismissTour(h.page)
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    const prose = 'Ths are a awkward sentence.'
    await h.rpc('scenes/write', [chapter.guid, scene.id, `<p>${prose}</p>`, prose])
    await h.page.evaluate(chapter => window.novalistStores.project.getState().openScene(chapter.guid, chapter.scenes[0].id), chapter)
    const frame = h.page.frameLocator('.editor-frame')
    const editor = frame.locator('#editor')
    await expect(editor).toHaveText(prose)
    await newestRequest(editor)
    const status = h.page.locator('.status-bar .status-proofing')
    await expect(status).toHaveAttribute('role', 'group')
    await expect(status.locator('.status-proofing-checking')).toBeVisible()
    await expect(frame.locator('#grammar-status-bar')).toBeHidden()

    await respond(editor, [
      issue(4, 3, 'grammar', 'Fixture grammar correction'),
      issue(8, 1, 'grammar', 'Fixture article correction'),
      issue(0, 3, 'spelling', 'Fixture spelling correction')
    ])
    const grammar = status.locator('[data-proofing-kind="grammar"]')
    const spelling = status.locator('[data-proofing-kind="spelling"]')
    await expect(grammar).toContainText('2')
    await expect(spelling).toContainText('1')
    await expect(status.locator('.status-proofing-checking')).toHaveCount(0)
    await grammar.click()
    await expect(frame.locator('#grammar-popup.visible .gp-message')).toHaveText('Fixture grammar correction')
    await frame.locator('.gp-dismiss').click()
    await spelling.click()
    await expect(frame.locator('#grammar-popup.visible .gp-message')).toHaveText('Fixture spelling correction')
    await frame.locator('.gp-dismiss').click()

    for (const width of [640, 510]) {
      await h.page.setViewportSize({ width, height: 650 })
      const geometry = await status.evaluate(element => {
        const badge = element.getBoundingClientRect()
        const footer = element.closest('.status-bar')!.getBoundingClientRect()
        const left = element.closest('.status-left')!.getBoundingClientRect()
        const center = document.querySelector('.status-center-wrap')!.getBoundingClientRect()
        return { left: badge.left, right: badge.right, top: badge.top, bottom: badge.bottom,
          leftGroupRight: left.right, centerLeft: center.left,
          footerLeft: footer.left, footerRight: footer.right, footerTop: footer.top, footerBottom: footer.bottom }
      })
      expect(geometry.left).toBeGreaterThanOrEqual(geometry.footerLeft)
      expect(geometry.right).toBeLessThanOrEqual(geometry.footerRight + 1)
      expect(geometry.top).toBeGreaterThanOrEqual(geometry.footerTop)
      expect(geometry.bottom).toBeLessThanOrEqual(geometry.footerBottom + 1)
      expect(geometry.right, `proofing is not clipped at ${width}px`).toBeLessThanOrEqual(geometry.leftGroupRight + 1)
      expect(geometry.right, `proofing does not cover the project overview at ${width}px`).toBeLessThanOrEqual(geometry.centerLeft + 1)
    }
    await h.page.screenshot({ path: '/tmp/novalist-status-proofing-510.png', animations: 'disabled' })
    await expect(frame.locator('#grammar-status-bar')).toBeHidden()

    await requestCheck(editor)
    await expect(status.locator('.status-proofing-checking')).toBeVisible()
    await respond(editor, [])
    await expect(status.locator('.status-proofing-clear')).toBeVisible()
    await expect(status.locator('[data-proofing-kind]')).toHaveCount(0)
    await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', { grammarCheckEnabled: false }))
    await expect(status).toHaveCount(0)
    await expect(frame.locator('#grammar-status-bar')).toBeHidden()
  } finally {
    await h.close()
  }
})

test('status proofing follows the focused scene and drops stale scene and unmounted pane results', async () => {
  const h = await launchApp('nl-status-proofing-panes-')
  try {
    await installCheckerFixture(h.page)
    const book = await seedBook(h, { Chapter: ['Left scene', 'Right scene', 'New scene'] })
    await dismissTour(h.page)
    await h.page.setViewportSize({ width: 1420, height: 900 })
    const chapter = book.chapters[0]
    const prose = ['Left are a awkward sentence.', 'Rihgt teh world.', 'A clean replacement scene.']
    for (const [index, scene] of chapter.scenes.entries()) {
      await h.rpc('scenes/write', [chapter.guid, scene.id, `<p>${prose[index]}</p>`, prose[index]])
    }
    const paneIds = await h.page.evaluate(async chapter => {
      const shell = window.novalistStores.shell.getState()
      const left = shell.activePaneId
      await window.novalistStores.project.getState().openSceneIn(left, chapter.guid, chapter.scenes[0].id)
      shell.splitActivePane('row')
      const right = window.novalistStores.shell.getState().activePaneId
      await window.novalistStores.project.getState().openSceneIn(right, chapter.guid, chapter.scenes[1].id)
      return [left, right]
    }, chapter)
    const editors = [0, 1].map(index => h.page.locator('.pane-leaf').nth(index).frameLocator('.editor-frame').locator('#editor'))
    await expect(editors[0]).toHaveText(prose[0])
    await expect(editors[1]).toHaveText(prose[1])
    await respond(editors[0], [issue(5, 3, 'grammar', 'Left grammar')])
    const rightIssues = [issue(0, 5, 'spelling', 'Right spelling'), issue(6, 3, 'spelling', 'Second spelling')]
    await respond(editors[1], rightIssues)
    const status = h.page.locator('.status-proofing')

    await editors[0].locator('p').click()
    await expect(status.locator('[data-proofing-kind="grammar"]')).toContainText('1')
    await expect(status.locator('[data-proofing-kind="spelling"]')).toHaveCount(0)
    await editors[1].locator('p').click()
    await expect(status.locator('[data-proofing-kind="spelling"]')).toContainText('2')
    await expect(status.locator('[data-proofing-kind="grammar"]')).toHaveCount(0)

    await requestCheck(editors[0])
    const leftIssues = [
      issue(5, 3, 'grammar', 'Left grammar'),
      issue(9, 1, 'grammar', 'Left article'),
      issue(11, 7, 'grammar', 'Left wording')
    ]
    await respond(editors[0], leftIssues)
    // A background pane's response must not replace the focused scene's count.
    await expect(status.locator('[data-proofing-kind="spelling"]')).toContainText('2')
    await expect(status.locator('[data-proofing-kind="grammar"]')).toHaveCount(0)
    await editors[0].locator('p').click()
    await expect(status.locator('[data-proofing-kind="grammar"]')).toContainText('3')
    await editors[1].locator('p').click()
    await expect(status.locator('[data-proofing-kind="spelling"]')).toContainText('2')

    const pending = await requestCheck(editors[1])
    await h.page.evaluate(({ paneId, chapterGuid, sceneId }) =>
      window.novalistStores.project.getState().openSceneIn(paneId, chapterGuid, sceneId),
    { paneId: paneIds[1], chapterGuid: chapter.guid, sceneId: chapter.scenes[2].id })
    await expect(editors[1]).toHaveText(prose[2])
    await respond(editors[1], rightIssues, pending.requestId)
    await expect(status.locator('[data-proofing-kind]')).toHaveCount(0)
    await expect(editors[1].locator('.grammar-issue')).toHaveCount(0)
    await requestCheck(editors[1])
    await respond(editors[1], [])
    await expect(status.locator('.status-proofing-clear')).toBeVisible()

    await h.page.evaluate(id => window.novalistStores.shell.getState().setPaneView(id, 'dashboard'), paneIds[1])
    await expect(h.page.locator('.editor-frame')).toHaveCount(1)
    await expect(status).toHaveCount(0)
    await h.page.evaluate(id => window.novalistStores.shell.getState().closePaneById(id), paneIds[1])
    await expect(h.page.locator('.pane-leaf')).toHaveCount(1)
    // Collapsing the split remounts the surviving editor. Its fresh check must
    // publish its own results, rather than retaining the closed pane's badge.
    const remainingEditor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(remainingEditor).toHaveText(prose[0])
    await newestRequest(remainingEditor)
    await expect(status.locator('.status-proofing-checking')).toBeVisible()
    await respond(remainingEditor, leftIssues)
    await expect(status.locator('[data-proofing-kind="grammar"]')).toContainText('3')
    await expect(status.locator('[data-proofing-kind="spelling"]')).toHaveCount(0)
    await h.page.evaluate(() => window.novalistStores.shell.getState().goHome())
    await expect(h.page.locator('.editor-frame')).toHaveCount(0)
    await expect(status).toHaveCount(0)
  } finally {
    await h.close()
  }
})
