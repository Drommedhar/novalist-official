import { test, expect } from '@playwright/test'
import { launchApp, seedBook, dismissTour } from './harness'

/**
 * Teaching the checker a word from the prose.
 *
 * "Add to Dictionary" in the editor's own menu used to reach LanguageTool's
 * dictionary alone - a paid service most writers have no account for - so the
 * word never reached the writer's own list, never reached the platform checker,
 * and the red underline the writer had just dismissed stayed exactly where it
 * was. The list in Settings stayed empty, which is how the defect was reported.
 */

test('a word taught from the prose lands in the writer own dictionary', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-dict-')
  try {
    await dismissTour(h.page)
    await seedBook(h, { 'Chapter One': ['Scene One'] })

    await h.page.locator('.binder-scene-row').first().click()
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toBeVisible({ timeout: 30_000 })
    await editor.click()
    // Focus restores scroll asynchronously; finish that before anchoring a menu.
    await expect.poll(() => editor.evaluate(element =>
      (element.ownerDocument.defaultView as unknown as {
        NovalistEditorState: { isFocusing: boolean }
      }).NovalistEditorState.isFocusing
    )).toBe(false)
    await h.page.keyboard.type('The Aelthorn banner burned. ')

    // Nothing was learned before the writer asked for it.
    expect(await h.rpc<string[]>('spell/words')).toEqual([])

    // Keep the synthetic menu event and spelling response in one browser task:
    // a native right-click followed by a separate evaluate can lose the menu
    // before the fixture response arrives. Anchor it to visible prose so the
    // dictionary action remains reachable after the editor's initial layout.
    const paragraph = editor.locator('p').filter({ hasText: 'The Aelthorn banner burned.' }).first()
    await paragraph.scrollIntoViewIfNeeded()
    await paragraph.evaluate((element) => {
      const doc = element.ownerDocument
      const rect = element.getBoundingClientRect()
      element.dispatchEvent(new MouseEvent('contextmenu', {
        bubbles: true,
        cancelable: true,
        clientX: rect.left + 30,
        clientY: rect.top + rect.height / 2
      }))
      ;(
        doc.defaultView as unknown as {
          setSpellingSuggestions(word: string, suggestions: string[]): void
        }
      ).setSpellingSuggestions('Aelthorn', ['Althorn'])
    })

    const addRow = h.page.frameLocator('.editor-frame').locator('[data-action="addToDictionary"]')
    await expect(addRow).toBeVisible({ timeout: 10_000 })
    await addRow.click()

    // Stored with the writer's settings, which is what Settings reads back and
    // what the platform checker is handed on every launch.
    await expect
      .poll(() => h.rpc<string[]>('spell/words'), { timeout: 15_000 })
      .toEqual(['Aelthorn'])
  } finally { await h.close() }
})
