import { expect, _electron as electron, type ElectronApplication, type Page } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * Launching the app and putting a small book in it.
 *
 * Most of what the audit calls a shipped feature is reachable from the RPC the
 * screens themselves call, so a spec that drives those is testing the same
 * route the writer takes without spelling out a click path that changes every
 * time the layout does. The screens are covered separately, where the screen
 * is the thing under test.
 */

export type Harness = {
  app: ElectronApplication
  page: Page
  workDir: string
  /** Runs a call in the page, with the RPC and stores already available. */
  rpc<T>(method: string, params?: unknown[]): Promise<T>
  close(): Promise<void>
}

export async function launchApp(
  prefix: string,
  /** Extra environment for the launch, e.g. NOVALIST_FORCE_MOBILE for the phone shell. */
  extraEnv: Record<string, string> = {}
): Promise<Harness> {
  const workDir = mkdtempSync(join(tmpdir(), prefix))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')
  Object.assign(env, extraEnv)

  // A fresh Chromium profile per launch. The settings dir above isolates what
  // the backend writes, but everything the renderer remembers per machine -
  // saved pane layouts, panel widths, the last workspace - lives in
  // localStorage, which is in the profile. Sharing one profile let a layout
  // saved by one spec turn up in the next, so a test could pass or fail
  // depending on which ones had run before it.
  const app = await electron.launch({
    args: ['out/main/index.js', `--user-data-dir=${join(workDir, 'profile')}`],
    env
  })
  const page = await app.firstWindow()
  // The phone shell has no status bar to read the connection off, so it waits
  // for its own root and for the RPC client the backend indicator stands for.
  if (extraEnv.NOVALIST_FORCE_MOBILE === '1') {
    // .shell.mobile is the root class; .mobile-shell only exists once a project
    // is open, which is after this point.
    await expect(page.locator('.shell.mobile')).toBeVisible({ timeout: 30_000 })
    await page.waitForFunction(() => !!window.novalistRpc, undefined, { timeout: 30_000 })
  } else {
    // The status bar is contextual chrome and is intentionally absent before a
    // project opens (including project-less Settings). Read the same backend
    // readiness state directly instead of making every desktop test depend on
    // that particular visual surface being mounted.
    await page.waitForFunction(
      () =>
        Boolean(
          window.novalistRpc && window.novalistStores?.shell.getState().backendVersion
        ),
      undefined,
      { timeout: 30_000 }
    )
  }

  const rpc = <T,>(method: string, params: unknown[] = []): Promise<T> =>
    page.evaluate(
      (a: { m: string; p: unknown[] }) => window.novalistRpc.request(a.m, a.p),
      { m: method, p: params }
    ) as Promise<T>

  return { app, page, workDir, rpc, close: () => app.close() }
}

/**
 * Sizes the window, in Electron's display-independent pixels.
 *
 * The app opens maximised. On Windows, setBounds() on a maximised window is
 * silently ignored: the window stayed full-screen, the renderer stayed above
 * every phone breakpoint, and a spec that believed it was looking at a 393px
 * phone was actually asserting against the desktop shell. Where the assertion
 * happened to hold at both sizes it passed for the wrong reason; where it did
 * not, it failed for a reason that had nothing to do with the code under test.
 * Leaving the maximised state first is what makes the requested size take
 * effect, and waiting on the viewport is what proves it did.
 */
export async function resizeWindow(
  h: Harness,
  width: number,
  height: number,
  minimum: [number, number] = [300, 300]
): Promise<void> {
  await h.app.evaluate(
    async ({ BrowserWindow }, s: { w: number; h: number; min: [number, number] }) => {
      const win = BrowserWindow.getAllWindows()[0]
      if (win.isFullScreen()) win.setFullScreen(false)
      if (win.isMaximized()) win.unmaximize()
      win.setMinimumSize(s.min[0], s.min[1])
      win.setBounds({ width: s.w, height: s.h })
    },
    { w: width, h: height, min: minimum }
  )
  // The OS applies the new bounds asynchronously; the viewport is the thing the
  // specs actually measure, so wait for that rather than for the bounds call.
  await h.page.waitForFunction(
    (w: number) => Math.abs(window.innerWidth - w) <= 40,
    width,
    { timeout: 15_000 }
  )
}

/**
 * Closes the first-run tour if this profile is being offered it.
 *
 * A fresh profile opens the tour over the content, and its card takes the
 * pointer events for everything it covers - which at a phone width is most of
 * the screen. A spec about layout is not a spec about onboarding, so it
 * dismisses the tour rather than working around it at every interaction.
 */
export async function dismissTour(page: Page): Promise<void> {
  const tour = page.locator('.tour-card')
  try {
    await tour.waitFor({ state: 'visible', timeout: 2_000 })
    await tour.getByRole('button', { name: 'Close tour' }).click()
    await tour.waitFor({ state: 'hidden', timeout: 5_000 })
  } catch {
    // A migrated profile, or one whose tour is deliberately off, has nothing
    // to close - which is not a failure of the spec that called this.
  }
}

export type Book = {
  chapters: { guid: string; title: string; scenes: { id: string; title: string }[] }[]
}

/** The project as the binder holds it. */
export const state = (h: Harness): Promise<Book> => h.rpc<Book>('project/getState')

/**
 * A project with the chapters and scenes named, created through the same calls
 * the binder makes.
 */
export async function seedBook(
  h: Harness,
  layout: Record<string, string[]>,
  projectName = 'Spec'
): Promise<Book> {
  await evaluateWhenReady(h.page, async (a: { dir: string; name: string }) => {
    await window.novalistRpc.request('project/create', [a.dir, a.name, 'Book One'])
  }, { dir: h.workDir, name: projectName })

  for (const [chapter, scenes] of Object.entries(layout)) {
    const after = await h.rpc<Book>('project/createChapter', [chapter])
    const guid = after.chapters[after.chapters.length - 1].guid
    for (const scene of scenes) await h.rpc('project/createScene', [guid, scene])
  }

  const book = await state(h)
  await h.page.evaluate(
    (s: unknown) => window.novalistStores.project.getState().applyState(s as never),
    book
  )
  return book
}

/** Titles as a shape that reads in a failure message. */
export const shapeOf = (book: Book): Record<string, string[]> =>
  Object.fromEntries(book.chapters.map((c) => [c.title, c.scenes.map((s) => s.title)]))
