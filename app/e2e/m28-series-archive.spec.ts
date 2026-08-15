import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync, readFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * The archive is named for the project, and carried one book of it.
 *
 * For a trilogy that is two-thirds missing, and the document said nothing about
 * the fact. The closed books are read through the per-book accessors rather
 * than by switching to them, so nothing about the open book moves.
 */
test('the world archive carries every book of the project, not just the open one', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-series-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  const setup = await evaluateWhenReady(page, async (parent) => {
    const rpc = window.novalistRpc
    const created = (await rpc.request('project/create', [parent, 'Series', 'Book One'])) as {
      projectPath: string
      books: { id: string; name: string }[]
    }

    const seed = async (chapterTitle: string, sceneTitle: string, prose: string): Promise<void> => {
      let state = (await rpc.request('project/createChapter', [chapterTitle])) as {
        chapters: { guid: string; scenes: { id: string }[] }[]
      }
      const guid = state.chapters[state.chapters.length - 1].guid
      state = (await rpc.request('project/createScene', [guid, sceneTitle])) as typeof state
      const scenes = state.chapters.find((c) => c.guid === guid)!.scenes
      const scene = scenes[scenes.length - 1]
      await rpc.request('scenes/write', [guid, scene.id, `<p>${prose}</p>`, prose])
      await rpc.request('scenes/setSynopsis', [guid, scene.id, prose])
    }

    await seed('One', 'Arrival', 'The first book.')

    const second = (await rpc.request('project/createBook', ['Book Two'])) as {
      books: { id: string; name: string }[]
    }
    const two = second.books.find((b) => b.name === 'Book Two')!
    await rpc.request('project/switchBook', [two.id])
    await seed('Later', 'Elsewhere', 'The second book.')

    // Left on the first, so the second is genuinely the closed one.
    const one = second.books.find((b) => b.name === 'Book One')!
    const back = await rpc.request('project/switchBook', [one.id])
    window.novalistStores.project.getState().applyState(back as never)
    return { projectPath: created.projectPath, bookTwoId: two.id }
  }, workDir)
  await expect(page.locator('.mode-rail')).toBeVisible({ timeout: 30_000 })

  const out = join(workDir, 'series.json')
  const ok = await page.evaluate(
    ([path, chapters]) =>
      window.novalistRpc
        .request<{ success: boolean }>('export/run', [
          'WorldJson',
          path,
          'Series',
          'Ada',
          false,
          chapters
        ])
        .then((r) => r.success),
    [out, await page.evaluate(() =>
      window.novalistStores.project.getState().chapters.map((c) => c.guid)
    )] as const
  )
  expect(ok).toBe(true)

  const archive = JSON.parse(readFileSync(out, 'utf8')) as {
    scenes: { scene: string }[]
    otherBooks: { book: string; scenes: { scene: string; chapter: string }[] }[]
  }

  // The open book, as before.
  expect(archive.scenes.map((s) => s.scene)).toContain('Arrival')

  // And the one nobody opened.
  const volume = archive.otherBooks.find((b) => b.book === 'Book Two')
  expect(volume).toBeDefined()
  expect(volume!.scenes.map((s) => s.scene)).toContain('Elsewhere')
  expect(volume!.scenes[0].chapter).toBe('Later')

  // Reading it did not move the open book.
  expect(
    await page.evaluate(() => window.novalistStores.project.getState().projectName)
  ).toBe('Series')

  // A box set: both volumes in one manuscript, the open book first and each
  // further one announced by a heading of its own.
  const bookIds = [setup.bookTwoId]
  const boxSet = join(workDir, 'boxset.md')
  const built = await page.evaluate(
    ([path, chapters, ids]) =>
      window.novalistRpc
        .request<{ success: boolean }>('export/run', [
          'Markdown', path, 'Series', 'Ada', false, chapters,
          null, null, null, true, null, 1, null, null, null, null, null, ids
        ])
        .then((r) => r.success),
    [
      boxSet,
      await page.evaluate(() =>
        window.novalistStores.project.getState().chapters.map((c) => c.guid)
      ),
      bookIds
    ] as const
  )
  expect(built).toBe(true)

  const manuscript = readFileSync(boxSet, 'utf8')
  expect(manuscript).toContain('The first book.')
  expect(manuscript).toContain('The second book.')
  // The second volume is announced, and a level above a chapter so a contents
  // list nests it rather than laying eighty chapters out flat.
  expect(manuscript).toMatch(/^# Book Two$/m)
  expect(manuscript).toMatch(/^## Later$/m)
  expect(manuscript.indexOf('The first book.')).toBeLessThan(
    manuscript.indexOf('The second book.')
  )

  await app.close()
})
