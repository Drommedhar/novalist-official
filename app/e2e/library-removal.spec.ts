import { test, expect } from '@playwright/test'
import { existsSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, resizeWindow } from './harness'

test('removed books and projects stay off the bookshelf and restore without changing project files', async () => {
  const h = await launchApp('nl-library-removal-')
  try {
    await h.rpc('project/create', [h.workDir, 'Series', 'First Volume'])
    await h.rpc('project/createBook', ['Second Volume'])
    await h.rpc('project/create', [h.workDir, 'Standalone', 'A Single Book'])
    await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
    await dismissTour(h.page)
    await resizeWindow(h, 1300, 950)
    const recents = await h.rpc<{ name: string; path: string }[]>('project/recent', [true])
    const manifests = recents.map(project => join(project.path, '.novalist', 'project.json'))
    const before = manifests.map(path => readFileSync(path, 'utf8'))
    const books = h.page.locator('.library-book')
    const search = h.page.getByRole('searchbox', { name: 'Find a project or book…' })
    const removeBook = async (name: string): Promise<void> => {
      await h.page.getByRole('button', { name: `Select ${name} — Series`, exact: true }).click()
      await h.page.getByRole('button', { name: `Remove book ${name} from bookshelf`, exact: true }).click()
    }
    const removeProject = async (name: string): Promise<void> => {
      await h.page.getByRole('button', { name: new RegExp(`^Select .* — ${name}$`) }).first().click()
      await h.page.getByRole('button', { name: `Remove project ${name} from bookshelf`, exact: true }).click()
    }
    const openRemoved = async (): Promise<void> => {
      await h.page.getByLabel('More project actions', { exact: true }).click()
      await h.page.getByRole('button', { name: 'Removed from bookshelf', exact: true }).click()
    }
    const dialog = h.page.getByRole('dialog', { name: 'Removed from bookshelf' })

    await expect(books).toHaveCount(3)
    await h.page.getByRole('button', { name: 'Add shelf', exact: true }).click()
    await h.page.getByRole('textbox', { name: 'Shelf name', exact: true }).fill('Writing')
    await h.page.getByRole('button', { name: 'Create shelf', exact: true }).click()
    await h.page.getByRole('button', { name: 'Select First Volume — Series' }).click()
    await h.page.getByRole('combobox', { name: 'Shelf for Series' }).selectOption({ label: 'Writing' })
    await h.page.getByRole('button', { name: 'Close book details' }).click()
    const shelf = h.page.getByRole('region', { name: 'Writing', exact: true })
    await removeBook('First Volume')
    await expect(books).toHaveCount(2)
    await expect(shelf.locator('.volume-name')).toHaveText(['Second Volume'])
    await expect(h.page.getByRole('button', { name: 'Undo', exact: true })).toBeFocused()
    await search.fill('First Volume')
    await expect(books).toHaveCount(0)
    await search.fill('')
    await h.page.evaluate(() => window.novalistStores.project.getState().loadRecents())
    await h.page.reload()
    await expect(books).toHaveCount(2)

    await removeProject('Series')
    await expect(books).toHaveCount(1)
    await expect(h.page.locator('.library-total')).toHaveText('1 project')
    await h.page.getByRole('button', { name: 'Undo', exact: true }).click()
    await expect(shelf.locator('.volume-name')).toHaveText(['Second Volume'])
    // Restoring the parent keeps the earlier per-book choice.
    await expect(books).toHaveCount(2)
    await removeBook('Second Volume')
    await expect(h.page.getByRole('button', { name: /^Select .* — Series$/ })).toHaveCount(0)
    await expect(h.page.locator('.library-total')).toHaveText('1 project')
    await h.page.reload()
    await expect(books).toHaveCount(1)
    await openRemoved()
    await expect(dialog.getByRole('listitem')).toHaveCount(2)
    await dialog.getByRole('button', { name: 'Restore Second Volume to bookshelf' }).click()
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(shelf.locator('.volume-name')).toHaveText(['Second Volume'])

    await removeProject('Series')
    await h.page.reload()
    await expect(books).toHaveCount(1)
    await openRemoved()
    await expect(dialog.getByRole('button', { name: 'Restore First Volume to bookshelf' })).toHaveCount(0)
    await dialog.getByRole('button', { name: 'Restore Series to bookshelf' }).click()
    await dialog.getByRole('button', { name: 'Restore First Volume to bookshelf' }).click()
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(shelf.locator('.volume-name')).toHaveText(['First Volume', 'Second Volume'])
    await removeProject('Series')
    await removeProject('Standalone')
    await expect(books).toHaveCount(0)
    await expect(h.page.locator('.library-empty')).toBeVisible()
    await expect(h.page.locator('.library-total')).toHaveText('0 projects')

    // Removal is a library preference, never a project or manuscript edit.
    expect(manifests.every(path => existsSync(path))).toBe(true)
    expect(manifests.map(path => readFileSync(path, 'utf8'))).toEqual(before)
    expect((await h.rpc<{ books: unknown[] }[]>('project/recent', [true])).map(project => project.books.length).sort()).toEqual([1, 2])
  } finally { await h.close() }
})
