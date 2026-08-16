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

  // Through the retry rather than a bare evaluate. For about a second after the
  // page loads, an evaluate that does real work can die with "Execution context
  // was destroyed" - see appReady.ts for everything that was ruled out. Only the
  // first call of a spec was guarded that way, so whichever call happened to
  // land next in that window failed instead: three specs died in `seedBook`,
  // creating a scene or reading the state back, rather than on anything they
  // were testing. The retry fires on that one error alone, so a genuine failure
  // inside the call still fails on the first attempt.
  const rpc = <T,>(method: string, params: unknown[] = []): Promise<T> =>
    evaluateWhenReady(
      page,
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
    // The tour walks the workspace and gives back the one it borrowed, and it
    // does that a task after it unmounts. Settling it here is what stops the
    // hand-back landing in the middle of whatever the spec set up next - which
    // is how a spec that had asked for Write found itself on the Dashboard.
    await page.evaluate(() => new Promise<void>((resolve) => setTimeout(resolve, 0)))
  } catch {
    // A migrated profile, or one whose tour is deliberately off, has nothing
    // to close - which is not a failure of the spec that called this.
  }
}

/**
 * Into the writing workspace.
 *
 * Opening a project lands on the Dashboard, which is about the book rather than
 * about the scene in front of you, so it carries no binder, no inspector and no
 * notes dock - those belong to the Write mode alone. A spec that stood its
 * project up itself, rather than through `seedBook`, says so here before it
 * reaches for any of them.
 */
export async function enterWriting(page: Page): Promise<void> {
  await page.waitForFunction(
    () =>
      new Promise<boolean>((resolve) => {
        const shell = window.novalistStores.shell.getState()
        if (shell.mainView !== 'write') shell.setMainView('write')
        // The shell sends a freshly opened project to the Dashboard from an
        // effect, and an effect can land a frame after the evaluate that asked
        // for Write - which put the spec back on a screen with no binder. Two
        // frames later is the first point at which "it stuck" is an honest
        // answer, and asking again is cheaper than guessing how long to wait.
        requestAnimationFrame(() =>
          requestAnimationFrame(() =>
            resolve(window.novalistStores.shell.getState().mainView === 'write')
          )
        )
      }),
    undefined,
    { timeout: 15_000 }
  )
}

export type Book = {
  chapters: { guid: string; title: string; scenes: { id: string; title: string }[] }[]
}

/**
 * What the app tried to put on the clipboard, or '' if nothing has.
 *
 * Under test the app never reaches the real clipboard - `playwright.config.ts`
 * sets NOVALIST_NO_CLIPBOARD for the whole run and main/dialogs.ts keeps the
 * text here instead. This is the only way to assert on a copy, which is the
 * point: the alternative cost somebody their notes.
 */
export const copiedText = (h: Harness): Promise<string> =>
  h.app.evaluate(() => (globalThis as unknown as { __copied?: string }).__copied ?? '')

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
  await evaluateWhenReady(
    h.page,
    (s: unknown) => window.novalistStores.project.getState().applyState(s as never),
    book as unknown
  )
  // A helper that creates chapters and scenes through the binder's own calls has
  // plainly put the spec in Write.
  await enterWriting(h.page)
  return book
}

/** Titles as a shape that reads in a failure message. */
export const shapeOf = (book: Book): Record<string, string[]> =>
  Object.fromEntries(book.chapters.map((c) => [c.title, c.scenes.map((s) => s.title)]))
