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

export async function launchApp(prefix: string): Promise<Harness> {
  const workDir = mkdtempSync(join(tmpdir(), prefix))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  const rpc = <T,>(method: string, params: unknown[] = []): Promise<T> =>
    page.evaluate(
      (a: { m: string; p: unknown[] }) => window.novalistRpc.request(a.m, a.p),
      { m: method, p: params }
    ) as Promise<T>

  return { app, page, workDir, rpc, close: () => app.close() }
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
