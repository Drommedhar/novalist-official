import { test, expect, type Page } from '@playwright/test'
import { resolve } from 'node:path'
import { pathToFileURL } from 'node:url'
import type { EditorWindow } from '../src/renderer/src/views/editor/editorBridge'
import { dismissTour, launchApp, seedBook } from './harness'

async function commentAtEnd(page: Page, html = '<p>Alpha beta lastword</p>'): Promise<void> {
  await page.evaluate((value) => {
    const w = window as unknown as EditorWindow
    w.setContent(value)
    const editor = document.getElementById('editor')!
    editor.focus()
    const walker = document.createTreeWalker(editor.querySelector('p')!, NodeFilter.SHOW_TEXT)
    let text = walker.nextNode()!
    while (walker.nextNode()) text = walker.currentNode
    const selection = window.getSelection()!
    selection.setBaseAndExtent(text, text.textContent!.length - 8, text, text.textContent!.length)
    w.addCommentToSelection('end-comment')
    w.setCommentsData([{ id: 'end-comment', anchorText: 'lastword', text: 'Check this word' }])
  }, html)
  await page.locator('#editor p').click()
  await page.keyboard.press('End')
}

test.describe('comment anchors in the shipped editor', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(pathToFileURL(resolve('src/renderer/public/editor/editor.html')).href)
  })

  for (const pageView of [false, true]) {
    test(`Enter after a commented last word does not highlight subsequent paragraphs (page view: ${pageView})`, async ({ page }) => {
      await page.evaluate((on) => (window as unknown as EditorWindow).setPageView(on), pageView)
      await commentAtEnd(page)
      await page.keyboard.press('Enter')
      await page.keyboard.type('Next paragraph')
      await page.keyboard.press('Enter')
      await page.keyboard.type('Still ordinary text')

      await expect(page.locator('#editor p')).toHaveText([
        'Alpha beta lastword', 'Next paragraph', 'Still ordinary text'
      ])
      await expect(page.locator('#editor .nv-comment')).toHaveText(['lastword'])
      await expect(page.locator('#editor p').nth(1).locator('[style*="background"]')).toHaveCount(0)
    })
  }

  test('Enter and undo/redo preserve formatting without restoring the copied comment', async ({ page }) => {
    await commentAtEnd(page, '<p>Alpha beta <b><i>lastword</i></b></p>')
    await page.keyboard.press('Enter')
    await expect(page.locator('#editor p')).toHaveCount(2)
    await page.keyboard.press('ControlOrMeta+z')
    await expect(page.locator('#editor p')).toHaveCount(1)
    await expect(page.locator('#editor .nv-comment')).toHaveText(['lastword'])
    await page.keyboard.press('ControlOrMeta+Shift+z')
    await expect(page.locator('#editor p')).toHaveCount(2)
    await expect(page.locator('#editor .nv-comment')).toHaveText(['lastword'])
    await page.keyboard.type('Formatted continuation')
    await expect(page.locator('#editor p').nth(1).locator('b i')).toHaveText('Formatted continuation')
    await expect(page.locator('#editor .nv-comment')).toHaveText(['lastword'])
    await page.keyboard.press('ControlOrMeta+z')
    await expect(page.locator('#editor p').nth(1)).toHaveText('')
    await page.keyboard.press('ControlOrMeta+Shift+z')
    await expect(page.locator('#editor p').nth(1).locator('b i')).toHaveText('Formatted continuation')
    await expect(page.locator('#editor .nv-comment')).toHaveText(['lastword'])
  })

  test('splitting inside an anchor preserves the comments on both original text fragments', async ({ page }) => {
    await commentAtEnd(page)
    await page.evaluate(() => {
      const text = document.querySelector('#editor .nv-comment')!.firstChild!
      window.getSelection()!.setPosition(text, 4)
    })
    await page.keyboard.press('Enter')
    await expect(page.locator('#editor .nv-comment')).toHaveText(['last', 'word'])
    await page.evaluate(() => (window as unknown as EditorWindow).removeCommentById('end-comment'))
    await expect(page.locator('#editor .nv-comment')).toHaveCount(0)
    await expect(page.locator('#editor p')).toHaveText(['Alpha beta last', 'word'])
  })
})

test('deleting a margin comment removes its highlight and stays deleted after reopening', async () => {
  const h = await launchApp('nl-comment-delete-')
  try {
    await h.page.setViewportSize({ width: 1440, height: 900 })
    const book = await seedBook(h, { One: ['A', 'B'] })
    const { guid, scenes } = book.chapters[0]
    await dismissTour(h.page)
    await h.rpc('scenes/write', [guid, scenes[0].id, '<p>Alpha beta lastword</p>', 'Alpha beta lastword'])
    const open = async (sceneId: string): Promise<void> => {
      await h.page.evaluate(({ guid, sceneId }) =>
        window.novalistStores.project.getState().openScene(guid, sceneId), { guid, sceneId })
    }
    await open(scenes[0].id)
    const frame = h.page.frameLocator('.editor-frame')
    const editor = frame.locator('#editor')
    await expect(editor).toHaveText('Alpha beta lastword', { timeout: 30_000 })
    await editor.evaluate((el) => {
      const text = el.querySelector('p')!.firstChild!
      ;(el as HTMLElement).focus()
      el.ownerDocument.getSelection()!.setBaseAndExtent(text, 11, text, 19)
    })
    await frame.locator('#ft-comment').click()
    await expect(frame.locator('.nv-cc-card')).toBeVisible()
    await expect(editor.locator('.nv-comment')).toHaveText('lastword')
    // A split inside the commented word leaves two legitimate fragments.
    // Enter at its end must not produce a third one.
    await editor.evaluate((el) => {
      ;(el as HTMLElement).focus()
      el.ownerDocument.getSelection()!.setPosition(el.querySelector('.nv-comment')!.firstChild!, 4)
    })
    await h.page.keyboard.press('Enter')
    await expect(editor.locator('.nv-comment')).toHaveText(['last', 'word'])
    await h.page.keyboard.press('End')
    await h.page.keyboard.press('Enter')
    await h.page.keyboard.type('Plain follow-up')
    await expect(editor.locator('.nv-comment')).toHaveText(['last', 'word'])
    await frame.locator('.nv-cc-del').click()

    await expect.poll(async () => (await h.rpc<{ comments: unknown[] }>(
      'scenes/getAnnotations', [guid, scenes[0].id]
    )).comments).toEqual([])
    await expect(editor.locator('.nv-comment')).toHaveCount(0)
    await expect(frame.locator('.nv-cc-card')).toHaveCount(0)
    const paragraphs = ['Alpha beta last', 'word', 'Plain follow-up']
    await expect(editor.locator('p')).toHaveText(paragraphs)
    await expect.poll(async () => {
      const { html } = await h.rpc<{ html: string }>('scenes/read', [guid, scenes[0].id])
      return { saved: html.includes('Plain follow-up'), highlighted: html.includes('nv-comment') }
    }).toEqual({ saved: true, highlighted: false })

    await open(scenes[1].id)
    await expect(editor).not.toContainText('Alpha')
    await open(scenes[0].id)
    await expect(editor.locator('p')).toHaveText(paragraphs)
    await expect(editor.locator('.nv-comment')).toHaveCount(0)
    await expect(frame.locator('.nv-cc-card')).toHaveCount(0)
    await editor.click()
    await h.page.keyboard.press('End')
    await h.page.keyboard.press('Enter')
    await h.page.keyboard.type('Ordinary text after deletion')
    await expect(editor.locator('.nv-comment')).toHaveCount(0)
    await expect(editor).toContainText('Ordinary text after deletion')
  } finally {
    await h.close()
  }
})

type AnnotationGateWindow = typeof window & {
  annotationGate: { pending: number; delivered: number; release(): void }
}

for (const action of ['add comment', 'switch scene']) {
  test(`a delayed annotation list cannot clear highlights after ${action}`, async () => {
    const h = await launchApp('nl-comment-delay-')
    try {
      const book = await seedBook(h, { One: ['A', 'B'] })
      const { guid, scenes } = book.chapters[0]
      await dismissTour(h.page)
      await h.rpc('scenes/write', [guid, scenes[0].id, '<p>Alpha beta lastword</p>', 'Alpha beta lastword'])
      await h.rpc('scenes/write', [guid, scenes[1].id,
        '<p><span class="nv-comment" data-comment-id="other">Other scene</span></p>', 'Other scene'
      ])
      await h.rpc('scenes/setAnnotations', [guid, scenes[1].id,
        [{ id: 'other', anchorText: 'Other scene', text: 'Other note', resolved: false }], []
      ])
      await h.page.evaluate((heldScene) => {
        const w = window as AnnotationGateWindow
        const client = window.novalistRpc
        const original = client.request.bind(client)
        let release!: () => void
        const gate = new Promise<void>(resolve => { release = resolve })
        w.annotationGate = { pending: 0, delivered: 0, release: () => { client.request = original; release() } }
        client.request = async <T>(method: string, params?: unknown): Promise<T> => {
          const result = await original<T>(method, params)
          if (method === 'scenes/getAnnotations' && (params as string[])[1] === heldScene) {
            w.annotationGate.pending++
            await gate
            w.annotationGate.delivered++
          }
          return result
        }
      }, scenes[0].id)
      await h.page.evaluate(({ guid, sceneId }) =>
        window.novalistStores.project.getState().openScene(guid, sceneId), { guid, sceneId: scenes[0].id })
      const frame = h.page.frameLocator('.editor-frame')
      const editor = frame.locator('#editor')
      await expect(editor).toHaveText('Alpha beta lastword')
      await expect.poll(() => h.page.evaluate(() => (window as AnnotationGateWindow).annotationGate.pending))
        .toBeGreaterThan(0)

      if (action === 'add comment') {
        await editor.evaluate((el) => {
          ;(el as HTMLElement).focus()
          const text = el.querySelector('p')!.firstChild!
          el.ownerDocument.getSelection()!.setBaseAndExtent(text, 11, text, 19)
        })
        await frame.locator('#ft-comment').click()
      } else {
        await h.page.evaluate(({ guid, sceneId }) =>
          window.novalistStores.project.getState().openScene(guid, sceneId), { guid, sceneId: scenes[1].id })
        await expect(editor).toHaveText('Other scene')
      }
      const anchor = action === 'add comment' ? 'lastword' : 'Other scene'
      await expect(frame.locator('.nv-cc-card')).toHaveCount(1)
      await expect(editor.locator('.nv-comment')).toHaveText([anchor])
      await h.page.evaluate(async () => {
        ;(window as AnnotationGateWindow).annotationGate.release()
        // Let the released RPC and React's follow-up effects finish.
        await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())))
      })
      expect(await h.page.evaluate(() => {
        const gate = (window as AnnotationGateWindow).annotationGate
        return gate.delivered === gate.pending
      })).toBe(true)
      await expect(frame.locator('.nv-cc-card')).toHaveCount(1)
      await expect(editor.locator('.nv-comment')).toHaveText([anchor])
    } finally {
      await h.close()
    }
  })
}

test('reopening repairs orphaned comment highlights and keeps existing comments and formatting', async () => {
  const h = await launchApp('nl-comment-repair-')
  try {
    const book = await seedBook(h, { One: ['A'] })
    const { guid, scenes } = book.chapters[0]
    const sceneId = scenes[0].id
    await dismissTour(h.page)
    await h.rpc('scenes/write', [guid, sceneId,
      '<p><span class="nv-comment nv-comment-active" data-comment-id="deleted"><b>Old anchor</b></span></p>' +
      '<p><span class="nv-comment" data-comment-id="deleted">Accidental continuation</span></p>' +
      '<p><span class="nv-comment" data-comment-id="keep">Keep this</span></p>',
      'Old anchor Accidental continuation Keep this'
    ])
    await h.rpc('scenes/setAnnotations', [guid, sceneId,
      [{ id: 'keep', anchorText: 'Keep this', text: 'Keep this note', resolved: false }], []
    ])
    await h.page.evaluate(({ guid, sceneId }) =>
      window.novalistStores.project.getState().openScene(guid, sceneId), { guid, sceneId })
    const frame = h.page.frameLocator('.editor-frame')
    const editor = frame.locator('#editor')
    await expect(editor.locator('.nv-comment')).toHaveText(['Keep this'])
    await expect(editor.locator('[data-comment-id="deleted"]')).toHaveCount(0)
    await expect(editor.locator('b')).toHaveText('Old anchor')
    await expect(editor.locator('p')).toHaveText(['Old anchor', 'Accidental continuation', 'Keep this'])
    await expect(frame.locator('.nv-cc-card')).toHaveCount(1)
    await expect.poll(async () => (await h.rpc<{ html: string }>(
      'scenes/read', [guid, sceneId]
    )).html).not.toContain('data-comment-id="deleted"')

    // The inspector's deletion route must clear the remaining mark too.
    await h.page.evaluate(() => window.novalistStores.shell.getState().setInspectorTab('footnotes'))
    await h.page.locator('.annotation-comment').getByRole('button', { name: 'Delete', exact: true }).click()
    await expect(editor.locator('.nv-comment')).toHaveCount(0)
    await expect(frame.locator('.nv-cc-card')).toHaveCount(0)
    await expect(editor.locator('p')).toHaveText(['Old anchor', 'Accidental continuation', 'Keep this'])
  } finally {
    await h.close()
  }
})
