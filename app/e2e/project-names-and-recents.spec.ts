import { test, expect } from '@playwright/test'
import { rmSync } from 'node:fs'
import { launchApp, seedBook, dismissTour } from './harness'

test('new and deleted projects update the welcome screen without restarting', async () => {
  const h = await launchApp('nl-recent-lifecycle-')
  try {
    await dismissTour(h.page)
    await seedBook(h, { One: ['A'] }, 'Recent Novel')
    await dismissTour(h.page)
    const { projectPath } = await h.rpc<{ projectPath: string }>('project/getState')
    await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
    await expect(h.page.locator('.start-recent-name')).toHaveText('Recent Novel')

    rmSync(projectPath, { recursive: true, force: true })
    // Returning from the file manager refreshes both the welcome screen and menu.
    await h.page.evaluate(() => window.dispatchEvent(new Event('focus')))
    await expect(h.page.locator('.start-recent-card')).toHaveCount(0)
  } finally {
    await h.close()
  }
})

test('project renaming updates the mounted dashboard and recent project name', async () => {
  const h = await launchApp('nl-project-rename-')
  try {
    await dismissTour(h.page)
    await seedBook(h, { One: ['A'] }, 'Old Project')
    await dismissTour(h.page)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
    await expect(h.page.locator('.dashboard-title')).toHaveText('Old Project')
    await h.page.locator('.toolbar-book').dblclick()
    const dialog = h.page.locator('.dialog-card')
    await dialog.locator('.dialog-input').fill('New Project')
    await dialog.getByRole('button', { name: 'OK', exact: true }).click()
    await expect(h.page.locator('.dashboard-title')).toHaveText('New Project')
    await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
    await expect(h.page.locator('.start-recent-name')).toHaveText('New Project')
  } finally {
    await h.close()
  }
})

test('the book picker renames the selected book and preserves its scenes', async () => {
  const h = await launchApp('nl-book-rename-')
  try {
    await dismissTour(h.page)
    await seedBook(h, { One: ['A'] })
    await dismissTour(h.page)
    const picker = h.page.locator('select[data-command="project.newBook"]')
    await picker.selectOption('__rename__')
    const dialog = h.page.locator('.dialog-card')
    await expect(dialog.locator('.dialog-input')).toHaveValue('Book One')
    await dialog.locator('.dialog-input').fill('A New Book Title')
    await dialog.getByRole('button', { name: 'OK', exact: true }).click()
    await expect(picker.locator('option:checked')).toHaveText('A New Book Title')
    await expect(h.page.locator('.toolbar-book')).toHaveText('Spec')
    const state = await h.rpc<{ chapters: { title: string; scenes: { title: string }[] }[] }>('project/getState')
    expect(state.chapters[0].scenes[0].title).toBe('A')
  } finally {
    await h.close()
  }
})
