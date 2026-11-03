import { expect, test, type Page } from '@playwright/test'
import { dismissTour, launchApp, resizeWindow, seedBook, type Harness } from './harness'

const notice = (page: Page) => page.getByRole('complementary', { name: 'Help Novalist grow' })
const supportDialog = (page: Page) => page.getByRole('dialog', { name: 'Support Novalist', exact: true })

async function openFromHelp(h: Harness): Promise<void> {
  await expect.poll(() => h.app.evaluate(({ Menu }) => {
    const help = Menu.getApplicationMenu()?.items.find(item => item.role === 'help')
    return help?.submenu?.items.find(item => item.label === 'Support Novalist')?.enabled
  })).toBe(true)
  await h.app.evaluate(({ Menu, BrowserWindow }) => {
    const help = Menu.getApplicationMenu()?.items.find(item => item.role === 'help')
    const support = help?.submenu?.items.find(item => item.label === 'Support Novalist')
    if (!support) throw new Error('Support menu item missing')
    support.click(support, BrowserWindow.getAllWindows()[0], {})
  })
  await expect(supportDialog(h.page)).toBeVisible()
}

test('bookshelf support opens both providers directly and restores keyboard focus', async () => {
  const h = await launchApp('nl-support-links-')
  try {
    const { page } = h
    await resizeWindow(h, 1200, 800)
    await expect(notice(page)).toBeInViewport()
    await expect(page.getByRole('button', { name: 'New Project', exact: true }).first()).toBeInViewport()
    await page.screenshot({ path: 'test-results/support-bookshelf.png', animations: 'disabled' })
    await h.app.evaluate(({ shell }) => {
      const state = globalThis as typeof globalThis & { supportUrls: string[] }
      state.supportUrls = []
      shell.openExternal = async (url) => { state.supportUrls.push(url) }
    })

    const trigger = notice(page).getByRole('button', { name: 'Support Novalist', exact: true })
    await trigger.click()
    const dialog = supportDialog(page)
    await expect(dialog).toBeVisible()
    await expect(page.locator('.about-view')).toHaveCount(0)
    await expect(dialog.getByRole('link')).toHaveCount(2)
    for (const [name, url] of [
      ['Patreon', 'https://patreon.com/Drommedhar'],
      ['Ko-fi', 'https://ko-fi.com/drommedhar']
    ]) {
      const link = dialog.getByRole('link', { name, exact: true })
      await expect(link).toHaveAttribute('href', url)
      await link.click()
    }
    await expect.poll(() => h.app.evaluate(() =>
      (globalThis as typeof globalThis & { supportUrls: string[] }).supportUrls
    )).toEqual(['https://patreon.com/Drommedhar', 'https://ko-fi.com/drommedhar'])
    await dialog.getByRole('button', { name: 'Close', exact: true }).focus()
    await page.keyboard.press('Tab')
    await expect(dialog.getByRole('link', { name: 'Patreon', exact: true })).toBeFocused()
    await page.screenshot({ path: 'test-results/support-dialog.png', animations: 'disabled' })
    await page.keyboard.press('Escape')
    await expect(dialog).toBeHidden()
    await expect(trigger).toBeFocused()
    await expect(notice(page)).toBeVisible()

    await trigger.click()
    await h.app.evaluate(({ shell }) => {
      shell.openExternal = async () => { throw new Error('Browser unavailable in test') }
    })
    await dialog.getByRole('link', { name: 'Patreon', exact: true }).click()
    await expect(dialog.getByRole('alert')).toContainText('The link could not be opened')
    await dialog.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(trigger).toBeFocused()

    await resizeWindow(h, 800, 650)
    for (const button of await notice(page).getByRole('button').all()) await expect(button).toBeInViewport()
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
  } finally {
    await h.close()
  }
})

test('monthly dismissal survives reload and returns on the next bookshelf visit after 30 days', async () => {
  const h = await launchApp('nl-support-month-')
  try {
    const { page } = h
    await page.clock.setFixedTime(new Date('2030-01-01T12:00:00Z'))
    await notice(page).getByRole('button', { name: 'Remind me in a month' }).click()
    await expect(notice(page)).toBeHidden()
    await page.reload()
    await expect(page.locator('.project-library')).toBeVisible()
    await expect(notice(page)).toBeHidden()

    await page.clock.setFixedTime(new Date('2030-01-31T11:59:59Z'))
    await page.reload()
    await expect(page.locator('.project-library')).toBeVisible()
    await expect(notice(page)).toBeHidden()
    await page.clock.setFixedTime(new Date('2030-01-31T12:00:00Z'))
    await expect(notice(page)).toBeHidden()

    await page.evaluate(() => window.novalistStores.shell.getState().setMainView('about'))
    await expect(page.locator('.about-view')).toBeVisible()
    await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))
    await expect(notice(page)).toBeVisible()
  } finally {
    await h.close()
  }
})

test('permanent dismissal survives time and reload while Help stays available with or without a project', async () => {
  const h = await launchApp('nl-support-never-')
  try {
    const { page } = h
    await notice(page).getByRole('button', { name: 'Don’t show again' }).click()
    await expect(notice(page)).toBeHidden()
    await page.clock.setFixedTime(new Date('2040-01-01T12:00:00Z'))
    await page.reload()
    await expect(page.locator('.project-library')).toBeVisible()
    await expect(notice(page)).toBeHidden()
    await openFromHelp(h)
    await supportDialog(page).getByRole('button', { name: 'Close', exact: true }).click()

    await seedBook(h, { Chapter: ['Scene'] })
    await dismissTour(page)
    await expect(notice(page)).toBeHidden()
    await expect(supportDialog(page)).toBeHidden()
    await openFromHelp(h)
    await supportDialog(page).getByRole('button', { name: 'Close', exact: true }).click()
    await page.evaluate(() => window.novalistStores.project.getState().closeProject())
    await expect(page.locator('.project-library')).toBeVisible()
    await expect(notice(page)).toBeHidden()
  } finally {
    await h.close()
  }
})
