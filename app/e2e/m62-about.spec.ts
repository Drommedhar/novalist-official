import { test, expect } from '@playwright/test'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook } from './harness'

/** What the shipped manifest says this build is. */
const MANIFEST_VERSION = (
  JSON.parse(readFileSync(join(__dirname, '..', 'package.json'), 'utf8')) as { version: string }
).version

/**
 * About: the screen that answers "what am I running, and who made the parts".
 *
 * Three things it has to get right, because each of them was previously either
 * absent or only reachable somewhere nobody looks:
 *
 *  - both versions, the app's and the core process's, side by side;
 *  - the attributions the bundled typefaces and runtimes require, which the app
 *    discharged nowhere at all;
 *  - a support block on the clipboard that carries versions and sizes and no
 *    trace of the writer's project - the same content-free rule the diagnostic
 *    log follows.
 */

test('about names both versions, credits what it bundles, and copies a content-free report', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-about-')
  await seedBook(h, { 'Chapter One': ['Scene A'] }, 'Aurelian Spec')
  const page = h.page
  await dismissTour(page)

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('about' as never))
  await expect(page.locator('.about-view')).toBeVisible({ timeout: 15_000 })

  // The app's version comes from the main process; the core's from the backend
  // handshake the harness already waited for. Both are versions, not blanks -
  // and the app's is Novalist's own rather than Electron's, which is what
  // app.getVersion() answers with in an unpackaged run.
  const appVersion = page.getByTestId('about-app-version')
  const coreVersion = page.getByTestId('about-core-version')
  await expect(appVersion).toHaveText(MANIFEST_VERSION, { timeout: 15_000 })
  await expect(coreVersion).toHaveText(/\d+\.\d+/)

  // Attribution is an obligation: every bundled typeface and runtime is named
  // with the terms it is under.
  const licences = page.getByTestId('about-licenses')
  await expect(licences.locator('li')).toHaveCount(5)
  const licenceText = (await licences.innerText()).replace(/\s+/g, ' ')
  for (const component of ['Fraunces', 'Newsreader', 'Courier Prime', 'Electron', '.NET']) {
    expect(licenceText, `${component} should be credited`).toContain(component)
  }
  expect(licenceText).toContain('SIL Open Font License 1.1')
  expect(licenceText).toContain('MIT License')

  // What's new is the repo's changelog, bundled rather than linked.
  await expect(page.getByTestId('about-changelog').locator('h2').first()).toBeVisible()

  await page.getByTestId('about-copy-system-info').click()

  const clipboard = async (): Promise<string> =>
    h.app.evaluate(({ clipboard: c }) => c.readText())
  await expect.poll(clipboard, { timeout: 15_000 }).toContain('Core process')

  const report = await clipboard()
  expect(report).toContain(`Novalist ${MANIFEST_VERSION}`)
  expect(report).toMatch(/^Platform \S+/m)
  expect(report).toMatch(/^Window \d+ x \d+$/m)

  // Content-free: no project name, no folder, nothing that looks like a path.
  expect(report).not.toContain('Aurelian')
  expect(report).not.toContain(h.workDir)
  expect(report, 'no Windows drive path').not.toMatch(/[A-Za-z]:[\\/]/)
  expect(report, 'no POSIX path').not.toMatch(/(^|\s)\/[A-Za-z]/)

  // Check for updates has no home outside the Help menu today. About reaches
  // the very same check the menu item does, by asking for it the same way.
  await page.getByRole('button', { name: 'Check for updates' }).click()
  await expect(page.locator('.dialog-card').first()).toBeVisible({ timeout: 60_000 })

  await h.close()
})
