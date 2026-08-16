import { test, expect } from '@playwright/test'
import { launchApp, seedBook, type Harness } from './harness'

/**
 * Switching books has to take the whole window with it.
 *
 * The binder repainted because it reads the chapter list through a reactive
 * selector, but every panel that fetches its own rows keyed that fetch on the
 * project path - which does not change when the book does. Collections, saved
 * lists, labels, stages, plotlines and the Codex all resolve against the active
 * book on the backend, so they kept showing the previous book's rows until
 * something happened to unmount them. "Change tabs and come back" was the fix
 * the writer had, and it is not one - so these assert without changing tabs.
 */

type Books = { books: { id: string; name: string }[] }

async function switchTo(h: Harness, name: string): Promise<void> {
  const state = await h.rpc<Books>('project/getState')
  const target = state.books.find((b) => b.name === name)
  expect(target, `book ${name} exists`).toBeTruthy()
  await h.page.evaluate(
    (id: string) => window.novalistStores.project.getState().switchBook(id),
    target!.id
  )
}

test('switching books repaints the panels beside the binder, not just the binder', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-bookswitch-')
  await seedBook(h, { 'Chapter One': ['Arrival'] })

  // A second book with a collection of its own, so the two books can never be
  // mistaken for one another.
  await h.rpc('project/createBook', ['Book Two'])
  await switchTo(h, 'Book Two')
  await h.rpc('collections/create', ['Two only', []])

  await switchTo(h, 'Book One')
  await h.rpc('collections/create', ['One only', []])

  // Open the tab once and never touch it again: unmounting is what used to
  // hide the bug.
  await h.page.locator('.binder-tab', { hasText: 'Collections' }).click()
  await expect(h.page.getByText('One only')).toBeVisible({ timeout: 15_000 })

  await switchTo(h, 'Book Two')

  await expect(h.page.getByText('Two only')).toBeVisible({ timeout: 15_000 })
  await expect(h.page.getByText('One only')).toHaveCount(0)

  await h.close()
})

test('the Codex follows the book without being navigated away from and back', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-bookswitch-codex-')
  await seedBook(h, { 'Chapter One': ['Arrival'] })

  await h.rpc('entities/create', ['character', 'Mira Vance'])

  await h.rpc('project/createBook', ['Book Two'])
  await switchTo(h, 'Book Two')
  await h.rpc('entities/create', ['character', 'Tomas Vance'])

  await switchTo(h, 'Book One')

  // The store the entry count is read from. It is shown outside the Codex view,
  // so it cannot wait for that view to be mounted again.
  await h.page.waitForFunction(
    () =>
      window.novalistStores.codex
        .getState()
        .entities.some((e) => e.name === 'Mira Vance'),
    undefined,
    { timeout: 15_000 }
  )

  const names = await h.page.evaluate(() =>
    window.novalistStores.codex.getState().entities.map((e) => e.name)
  )
  expect(names).toContain('Mira Vance')
  expect(names).not.toContain('Tomas Vance')

  await h.close()
})
