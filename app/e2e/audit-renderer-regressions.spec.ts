import { test, expect } from '@playwright/test'
import { launchApp, seedBook, dismissTour, selectCodexTab, resizeWindow } from './harness'

test('research drafts survive an immediate unmount and undo cannot cross note ownership', async () => {
  const h = await launchApp('nl-audit-research-')
  try {
    await seedBook(h, { One: ['A'] })
    await dismissTour(h.page)
    await h.rpc('research/save', [null, 'First note', 'Note', 'First original', [], []])
    await h.rpc('research/save', [null, 'Second note', 'Note', 'Second original', [], []])
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('research'))
    await h.page.locator('.codex-row', { hasText: 'First note' }).click()
    const prose = h.page.locator('.research-content .cm-content')
    await prose.fill('First draft survives navigation')
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
    await expect.poll(() => h.rpc('research/list')).toMatchObject([
      { content: 'First draft survives navigation' }, { content: 'Second original' }
    ])
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('research'))
    await h.page.locator('.codex-row', { hasText: 'First note' }).click()
    await prose.fill('First has undo history')
    await h.page.locator('.codex-row', { hasText: 'Second note' }).click()
    await expect(prose).toHaveText('Second original')
    await prose.press('ControlOrMeta+z')
    await expect(prose).toHaveText('Second original')
  } finally { await h.close() }
})

test('inline information is read-only and alternative replacements remain suggestions until accepted', async () => {
  const h = await launchApp('nl-audit-inline-')
  try {
    await seedBook(h, { One: ['A'] })
    await dismissTour(h.page)
    await h.page.evaluate(async () => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      window.novalistRpc.request = async function<T>(method: string, params?: unknown[]): Promise<T> {
        if (method === 'extensions/inlineActions') return ['information', 'alternatives'].map(id => ({ id, label: id, group: 'Audit' })) as T
        if (method === 'extensions/inlineAction/execute') return (params?.[0] === 'information'
          ? { text: 'A definition to read', disposition: 'information', alternatives: [], asSuggestion: false }
          : { text: 'First replacement', disposition: 'replace', alternatives: ['Chosen replacement'], asSuggestion: true }) as T
        return original<T>(method, params)
      }
      await window.novalistStores.extensions.getState().refreshContributions()
    })
    await h.page.evaluate(async () => {
      const state = window.novalistStores.project.getState()
      const chapter = state.chapters[0]
      await state.openScene(chapter.guid, chapter.scenes[0].id)
    })
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toBeVisible()
    const invoke = async (id: string) => editor.evaluate((element, actionId) => {
      const frame = window as unknown as { setContent(html: string): void; triggerInlineAction(id: string): void }
      frame.setContent('<p>Original prose</p>')
      const range = document.createRange()
      range.selectNodeContents(element.querySelector('p')!)
      const selection = window.getSelection()!
      selection.removeAllRanges()
      selection.addRange(range)
      frame.triggerInlineAction(actionId)
    }, id)
    await invoke('information')
    const dialog = h.page.getByRole('dialog', { name: 'Inline action' })
    await expect(dialog).toContainText('A definition to read')
    await expect(editor).toHaveText('Original prose')
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(editor).toHaveText('Original prose')
    await invoke('alternatives')
    await expect(dialog.getByRole('button', { name: 'Apply', exact: true })).toBeDisabled()
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
    await expect(editor).toHaveText('Original prose')
    await invoke('alternatives')
    await dialog.getByRole('radio', { name: 'Chosen replacement' }).check()
    await dialog.getByRole('button', { name: 'Apply', exact: true }).click()
    await expect(editor.locator('del[data-nl-change]')).toHaveText('Original prose')
    await expect(editor.locator('ins[data-nl-change]')).toHaveText('Chosen replacement')
  } finally { await h.close() }
})

test('an image-only scene survives editor loading and serialization', async () => {
  const h = await launchApp('nl-audit-image-only-')
  try {
    await seedBook(h, { One: ['A'] })
    await dismissTour(h.page)
    await h.page.evaluate(async () => {
      const state = window.novalistStores.project.getState()
      const chapter = state.chapters[0]
      await state.openScene(chapter.guid, chapter.scenes[0].id)
    })
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toBeVisible()
    const html = await editor.evaluate(() => {
      const frame = window as unknown as { setContent(html: string): void; getContent(): string }
      frame.setContent('<p><img data-nv-src="Images/cover.png" src="novalist-project://nl/Images/cover.png" alt="Cover"></p>')
      return frame.getContent()
    })
    expect(html).toContain('src="Images/cover.png"')
    await expect(editor.locator('img')).toHaveAttribute('alt', 'Cover')
  } finally { await h.close() }
})


test('an older research acknowledgement preserves the newer draft', async () => {
  const h = await launchApp('nl-audit-research-ack-')
  try {
    await seedBook(h, { One: ['A'] })
    await dismissTour(h.page)
    await h.rpc('research/save', [null, 'Source', 'Note', 'Original', [], []])
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('research'))
    await h.page.locator('.codex-row', { hasText: 'Source' }).click()
    await h.page.evaluate(() => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      let first = true
      window.novalistRpc.request = async function<T>(method: string, params?: unknown[]): Promise<T> {
        const result = await original<T>(method, params)
        if (method === 'research/save' && first) {
          first = false
          await new Promise<void>((resolve) => { (window as unknown as { releaseResearchSave(): void }).releaseResearchSave = resolve })
        }
        return result
      }
    })
    const prose = h.page.locator('.research-content .cm-content')
    await prose.fill('First pending save')
    await h.page.waitForFunction(() => typeof (window as unknown as { releaseResearchSave?: unknown }).releaseResearchSave === 'function')
    await prose.fill('Newer draft must remain')
    await h.page.evaluate(() => (window as unknown as { releaseResearchSave(): void }).releaseResearchSave())
    await expect(prose).toHaveText('Newer draft must remain')
    await expect.poll(() => h.rpc('research/list')).toMatchObject([{ content: 'Newer draft must remain' }])
  } finally { await h.close() }
})

test('conflict saving stays disabled while comparison is pending or failed', async () => {
  const h = await launchApp('nl-audit-conflict-load-')
  try {
    await seedBook(h, { One: ['A'] })
    await dismissTour(h.page)
    await h.page.evaluate(() => {
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      let first = true
      window.novalistRpc.request = function<T>(method: string, params?: unknown[]): Promise<T> {
        if (method === 'scenes/mergeRows' && first) {
          first = false
          return new Promise<T>((_resolve, reject) => {
            (window as unknown as { failComparison(): void }).failComparison = () => reject(new Error('Comparison unavailable'))
          })
        }
        return original<T>(method, params)
      }
      window.novalistStores.project.setState({ sceneConflict: { chapterGuid: 'c', sceneId: 's', mine: '<h2>Mine</h2>', theirs: '<p>Theirs</p>', plainText: 'Mine' } })
    })
    const dialog = h.page.locator('.scene-conflict-card')
    await expect(dialog.locator('.dialog-button.danger')).toBeDisabled()
    await h.page.evaluate(() => (window as unknown as { failComparison(): void }).failComparison())
    await expect(dialog.getByRole('alert')).toContainText('Comparison unavailable')
    await expect(dialog.locator('.dialog-button.danger')).toBeDisabled()
    await dialog.getByRole('button', { name: 'Try again' }).click()
    await expect(dialog.locator('.dialog-button.danger')).toBeEnabled()
    await dialog.press('Escape')
    await expect(dialog).toHaveCount(0)
  } finally { await h.close() }
})

test('palette keyboard navigation scrolls selected results and restores focus', async () => {
  const h = await launchApp('nl-audit-palette-')
  try {
    await seedBook(h, { One: ['A'] })
    await dismissTour(h.page)
    await h.page.evaluate(() => {
      const button = document.querySelector<HTMLButtonElement>('.mode-rail button')
      button?.focus()
      window.novalistStores.shell.setState({ commandPaletteOpen: true })
    })
    const dialog = h.page.locator('.palette-card')
    const input = dialog.getByRole('combobox')
    await expect(input).toBeFocused()
    for (let n = 0; n < 30; n++) await input.press('ArrowDown')
    const active = dialog.getByRole('option', { selected: true })
    await expect(active).toBeVisible()
    await expect.poll(() => active.evaluate((element) => {
      const row = element.getBoundingClientRect()
      const list = element.closest('.palette-results')!.getBoundingClientRect()
      return row.top >= list.top - 1 && row.bottom <= list.bottom + 1
    })).toBe(true)
    await input.press('Shift+Tab')
    expect(await dialog.evaluate((element) => element.contains(document.activeElement))).toBe(true)
    await h.page.keyboard.press('Escape')
    await expect(dialog).toHaveCount(0)
    expect(await h.page.evaluate(() => !!document.activeElement?.closest('.mode-rail'))).toBe(true)
    expect(await h.page.locator('.mode-rail').evaluate((element) => !!element.closest('[inert]'))).toBe(false)
  } finally { await h.close() }
})

test('invalid regular expressions show an error and a corrected search can retry', async () => {
  const h = await launchApp('nl-audit-find-error-')
  try {
    await seedBook(h, { One: ['A'] })
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.setState({ findReplaceOpen: true }))
    const dialog = h.page.locator('.findreplace-card')
    await dialog.locator('input').first().fill('[')
    await dialog.locator('.findreplace-options input[type=checkbox]').nth(2).check()
    await dialog.locator('.dialog-actions button').first().click()
    await expect(dialog.getByRole('alert')).toContainText('failed')
    await expect(dialog.locator('.findreplace-results')).toHaveCount(0)
    await dialog.locator('input').first().fill('valid')
    await dialog.locator('.dialog-actions button').first().click()
    await expect(dialog.getByRole('alert')).toHaveCount(0)
    await expect(dialog.locator('.findreplace-results')).toBeVisible()
  } finally { await h.close() }
})

test('restoring an entity revision reloads mounted alias, section, and relationship drafts', async () => {
  const h = await launchApp('nl-audit-codex-restore-')
  try {
    await seedBook(h, { One: ['A'] })
    await dismissTour(h.page)
    const entity = await h.rpc<{ id: string }>('entities/create', ['character', 'Mira'])
    await h.rpc('entities/updateLists', ['character', entity.id, ['Old alias'], [{ title: 'Old section', content: 'Old prose' }], [{ role: 'Old role', target: 'Companion' }]])
    await h.rpc('entities/updateLists', ['character', entity.id, ['New alias'], [{ title: 'New section', content: 'New prose' }], [{ role: 'New role', target: 'Companion' }]])
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('codex'))
    await h.page.locator('.codex-row', { hasText: 'Mira' }).click()
    await selectCodexTab(h.page, 'overview')
    await expect(h.page.locator('.entity-chip', { hasText: 'New alias' })).toBeVisible()
    await expect(h.page.locator('.entity-section-title')).toHaveValue('New section')
    await selectCodexTab(h.page, 'relationships')
    await expect(h.page.locator('.entity-rel-row input').first()).toHaveValue('New role')
    await selectCodexTab(h.page, 'details')
    const history = h.page.locator('.codex-match', { hasText: 'Earlier versions' })
    await history.locator('summary').click()
    await history.locator('.entity-history-row .dialog-button').first().click()
    await selectCodexTab(h.page, 'overview')
    await expect(h.page.locator('.entity-chip', { hasText: 'Old alias' })).toBeVisible()
    await expect(h.page.locator('.entity-section-title')).toHaveValue('Old section')
    await expect(h.page.locator('.entity-section .cm-content')).toHaveText('Old prose')
    await selectCodexTab(h.page, 'relationships')
    await expect(h.page.locator('.entity-rel-row input').first()).toHaveValue('Old role')
  } finally { await h.close() }
})

test('conflict choices preserve complete original and mixed block HTML', async () => {
  const h = await launchApp('nl-audit-conflict-markup-')
  try {
    await seedBook(h, { One: ['A'] })
    await dismissTour(h.page)
    const mine = '<h2>Mine heading</h2><ul><li>Mine list</li></ul>'
    const theirs = '<h2>Their heading</h2><table><tr><td>Their table</td></tr></table>'
    await h.page.evaluate(() => {
      window.novalistStores.project.setState({ resolveSceneConflict: async (html: string) => {
        ;(window as unknown as { resolvedHtml: string }).resolvedHtml = html
        window.novalistStores.project.setState({ sceneConflict: null })
      } })
    })
    const dialog = h.page.locator('.scene-conflict-card')
    for (const choice of ['mine', 'theirs', 'mixed']) {
      await h.page.evaluate(({ mine, theirs }) => {
        window.novalistStores.project.setState({ sceneConflict: { chapterGuid: 'c', sceneId: 's', mine, theirs, plainText: '' } })
      }, { mine, theirs })
      await expect(dialog.locator('.dialog-button.danger')).toBeEnabled()
      if (choice === 'theirs') await dialog.locator('.scene-conflict-actions button').nth(1).click()
      if (choice === 'mixed') await dialog.locator('.scene-conflict-row').last().locator('button').nth(1).click()
      await dialog.locator('.dialog-button.danger').click()
      await expect(dialog).toHaveCount(0)
      const html = await h.page.evaluate(() => (window as unknown as { resolvedHtml: string }).resolvedHtml)
      expect(html).toBe(choice === 'mine' ? mine : choice === 'theirs' ? theirs : '<h2>Mine heading</h2><table><tr><td>Their table</td></tr></table>')
    }
  } finally { await h.close() }
})

test('the mobile inspector traps keyboard focus and restores its trigger', async () => {
  const h = await launchApp('nl-audit-mobile-sheet-', { NOVALIST_FORCE_MOBILE: '1' })
  try {
    await seedBook(h, { One: ['A'] })
    await resizeWindow(h, 393, 852)
    await dismissTour(h.page)
    await h.page.evaluate(async () => {
      window.novalistStores.shell.getState().setMobileTab('manuscript')
      const state = window.novalistStores.project.getState()
      await state.openScene(state.chapters[0].guid, state.chapters[0].scenes[0].id)
    })
    const trigger = h.page.locator('.mobile-editor-inspector')
    await trigger.click()
    const sheet = h.page.locator('.mobile-sheet')
    await expect(sheet).toHaveAttribute('role', 'dialog')
    await expect(sheet).toHaveAttribute('aria-modal', 'true')
    await expect(sheet).toHaveAccessibleName('A')
    await expect(sheet.getByRole('button', { name: 'Close', exact: true })).toBeFocused()
    await h.page.keyboard.press('Shift+Tab')
    expect(await sheet.evaluate((element) => element.contains(document.activeElement))).toBe(true)
    expect(await trigger.evaluate((element) => !!element.closest('[inert]'))).toBe(true)
    await h.page.keyboard.press('Escape')
    await expect(sheet).toHaveCount(0)
    await expect(trigger).toBeFocused()
  } finally { await h.close() }
})
