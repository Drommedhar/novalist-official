import { test, expect, _electron as electron, type Page } from '@playwright/test'
import { cpSync, mkdirSync, mkdtempSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * Getting a book in, checked against files rather than fixtures.
 *
 * Import is the highest-risk surface in the project and the reason is on
 * record: the Scrivener reader had twenty green unit tests and 100% line
 * coverage, and produced an empty book from a real Scrivener project, because
 * every fixture had been written from the same wrong idea of the format as the
 * code. A parser tested only against its author's own fixtures is tested
 * against its author's own assumptions.
 *
 * So these build files the way the documented formats describe them - the
 * manual's rules, not the implementation's - and assert on what lands in the
 * project. The DOCX case round-trips Novalist's own export rather than
 * hand-building an OOXML package, which also checks the two halves agree.
 */

const RTF = (text: string): string =>
  `{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Times;}}\\f0\\fs24 ${text}\\par}`

const REAL_SCRIVENER_FIXTURE = join(
  process.cwd(),
  '..',
  'tests',
  'Fixtures',
  'Scrivener',
  'RealFormatting.scriv'
)

async function launch(workDir: string): Promise<{
  app: Awaited<ReturnType<typeof electron.launch>>
  page: Page
}> {
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })
  return { app, page }
}

async function newProject(page: Page, parent: string): Promise<void> {
  await evaluateWhenReady(page, async (dir: string) => {
    const state = await window.novalistRpc.request('project/create', [dir, 'Imported', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
  }, parent)
}

type Plan = {
  format: string
  chapterCount: number
  sceneCount: number
  chapters: { title: string; partTitle: string; scenes: { title: string }[] }[]
}

const preview = (page: Page, path: string): Promise<Plan> =>
  page.evaluate((p: string) => window.novalistRpc.request('manuscriptImport/preview', [p]), path) as Promise<Plan>

const runImport = (page: Page, path: string): Promise<{ chapters: number; scenes: number }> =>
  page.evaluate((p: string) => window.novalistRpc.request('manuscriptImport/run', [p]), path) as Promise<{
    chapters: number
    scenes: number
  }>

/** The book as the binder holds it after an import. */
const structure = (page: Page): Promise<{ title: string; scenes: string[] }[]> =>
  page.evaluate(async () => {
    const state = (await window.novalistRpc.request('project/getState')) as {
      chapters: { title: string; scenes: { title: string }[] }[]
    }
    return state.chapters.map((c) => ({ title: c.title, scenes: c.scenes.map((s) => s.title) }))
  })

test('every format the dialog advertises is one the reader accepts', async () => {
  test.setTimeout(120_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-imp-fmt-'))
  const { app, page } = await launch(workDir)

  const formats = (await page.evaluate(() =>
    window.novalistRpc.request('manuscriptImport/formats')
  )) as string[]

  // The dialog lists these to the writer before the picker opens, so a format
  // named here and unreadable is a promise the file picker cannot keep.
  expect(formats).toEqual(
    expect.arrayContaining(['.docx', '.odt', '.epub', '.md', '.markdown', '.txt', '.rtf', '.scriv'])
  )
  await app.close()
})

test('a Markdown manuscript splits on its headings and its ornament lines', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-imp-md-'))
  const file = join(workDir, 'book.md')
  // Exactly what docs/manual/38-manuscript-import.md promises for Markdown:
  // "# headings, paragraphs, and *** / --- scene breaks".
  writeFileSync(file, [
    '# Chapter One', '',
    'She arrived at dusk.', '',
    '***', '',
    'The inn was full.', '',
    '# Chapter Two', '',
    'They left before dawn.', ''
  ].join('\n'))

  const { app, page } = await launch(workDir)
  await newProject(page, workDir)

  const plan = await preview(page, file)
  expect(plan.chapterCount, 'the two # headings should be two chapters').toBe(2)
  expect(plan.chapters.map((c) => c.title)).toEqual(['Chapter One', 'Chapter Two'])
  // The ornament splits the first chapter and leaves the second alone.
  expect(plan.chapters[0].scenes.length, 'the *** should break chapter one in two').toBe(2)
  expect(plan.chapters[1].scenes.length).toBe(1)

  // Preview writes nothing.
  expect(await structure(page)).toEqual([])

  const result = await runImport(page, file)
  expect(result.chapters).toBe(2)
  expect(result.scenes).toBe(3)
  expect((await structure(page)).map((c) => c.title)).toEqual(['Chapter One', 'Chapter Two'])

  await app.close()
})

test('a plain-text manuscript finds its chapters without any markup', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-imp-txt-'))
  const file = join(workDir, 'book.txt')
  writeFileSync(file, [
    'Chapter 1', '',
    'She arrived at dusk.', '',
    'Chapter 2', '',
    'The inn was full.', ''
  ].join('\n'))

  const { app, page } = await launch(workDir)
  await newProject(page, workDir)

  const plan = await preview(page, file)
  expect(plan.chapterCount, 'two "Chapter N" lines should be two chapters').toBe(2)

  await runImport(page, file)
  expect((await structure(page)).length).toBe(2)

  await app.close()
})

test('a DOCX Novalist wrote is a DOCX Novalist can read back', async () => {
  test.setTimeout(240_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-imp-docx-'))
  const { app, page } = await launch(workDir)

  // A book to export, so the file under test is a real OOXML package rather
  // than one hand-built to match the reader's expectations.
  await evaluateWhenReady(page, async (dir: string) => {
    const rpc = window.novalistRpc
    let state = await rpc.request('project/create', [dir, 'RoundTrip', 'Book One'])
    for (const title of ['Chapter One', 'Chapter Two']) {
      state = await rpc.request('project/createChapter', [title])
      const chapters = (state as { chapters: { guid: string }[] }).chapters
      const guid = chapters[chapters.length - 1].guid
      state = await rpc.request('project/createScene', [guid, 'Scene'])
      const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
        .find((c) => c.guid === guid)!.scenes
      const line = title === 'Chapter One' ? 'She arrived at dusk.' : 'The inn was full.'
      await rpc.request('scenes/write', [guid, scenes[0].id, `<p>${line}</p>`, line])
    }
    window.novalistStores.project.getState().applyState(
      (await rpc.request('project/getState')) as never
    )
  }, workDir)

  const docx = join(workDir, 'book.docx')
  await page.evaluate(async (out: string) => {
    const state = window.novalistStores.project.getState()
    const guids = (state.chapters as { guid: string }[]).map((c) => c.guid)
    await window.novalistRpc.request('export/run', [
      'Docx', out, 'RoundTrip', 'A Writer', false, guids
    ])
  }, docx)

  const plan = await preview(page, docx)
  expect(plan.format, 'the reader did not recognise our own DOCX').not.toBe('')
  expect(plan.chapterCount, 'chapters did not survive the round trip').toBe(2)

  const before = (await structure(page)).length
  await runImport(page, docx)
  const after = await structure(page)

  // An import appends, so the book now holds both copies.
  expect(after.length).toBe(before + 2)
  expect(after.map((c) => c.title)).toContain('Chapter One')

  await app.close()
})

test('a Scrivener project imports through the app, not just through the reader', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-imp-scriv-'))
  const root = join(workDir, 'Book.scriv')
  mkdirSync(join(root, 'Files', 'Data', 'S1'), { recursive: true })
  mkdirSync(join(root, 'Files', 'Data', 'E1'), { recursive: true })

  writeFileSync(join(root, 'Book.scrivx'), `<?xml version="1.0"?>
<ScrivenerProject><Binder>
  <BinderItem UUID="D" Type="DraftFolder"><Title>Manuscript</Title><Children>
    <BinderItem UUID="C1" Type="Folder"><Title>Chapter</Title><Children>
      <BinderItem UUID="S1" Type="Text"><Title>Arrival</Title></BinderItem>
    </Children></BinderItem>
  </Children></BinderItem>
  <BinderItem UUID="CH" Type="Folder">
    <Title>Characters</Title>
    <MetaData><IconFileName>Characters (Photo)</IconFileName></MetaData>
    <Children><BinderItem UUID="E1" Type="Text"><Title>Mira Vance</Title></BinderItem></Children>
  </BinderItem>
  <BinderItem UUID="T" Type="TrashFolder"><Title>Trash</Title></BinderItem>
</Binder></ScrivenerProject>`)
  writeFileSync(join(root, 'Files', 'Data', 'S1', 'content.rtf'), RTF('She arrived at dusk.'))
  writeFileSync(join(root, 'Files', 'Data', 'S1', 'synopsis.txt'), 'She arrives.')
  writeFileSync(join(root, 'Files', 'Data', 'E1', 'content.rtf'), RTF('Mira Vance, harbourmaster.'))

  const { app, page } = await launch(workDir)
  await newProject(page, workDir)

  const plan = await preview(page, root)
  expect(plan.format).toBe('scrivener3')
  // The draft is the manuscript; the Characters folder is not a chapter.
  expect(plan.chapterCount).toBe(1)
  expect(plan.chapters[0].title).toBe('Chapter')

  await runImport(page, root)

  const chapters = await structure(page)
  expect(chapters.map((c) => c.title)).toEqual(['Chapter'])
  expect(chapters[0].scenes).toEqual(['Arrival'])

  // The sketch became a Codex entry rather than prose.
  const characters = (await page.evaluate(() =>
    window.novalistRpc.request('entities/list', ['character'])
  )) as { name: string }[]
  expect(characters.map((c) => c.name)).toContain('Mira Vance')

  await app.close()
})

test('a real-derived Scrivener document keeps punctuation, formatting, lists and a usable title', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-imp-scriv-real-'))
  const root = join(workDir, 'RealFormatting.scriv')
  cpSync(REAL_SCRIVENER_FIXTURE, root, { recursive: true })

  const { app, page } = await launch(workDir)
  await newProject(page, workDir)

  const plan = await preview(page, root)
  expect(plan.format).toBe('scrivener3')
  expect(plan.chapters[0].scenes[0].title).toBe('Scene 1')

  await runImport(page, root)
  const stored = (await page.evaluate(async () => {
    const state = (await window.novalistRpc.request('project/getState')) as {
      chapters: { guid: string; scenes: { id: string; title: string }[] }[]
    }
    const chapter = state.chapters[0]
    const scene = chapter.scenes[0]
    const content = (await window.novalistRpc.request('scenes/read', [chapter.guid, scene.id])) as {
      html: string
    }
    return { title: scene.title, html: content.html }
  })) as { title: string; html: string }

  expect(stored.title).toBe('Scene 1')
  expect(stored.html).toContain('<p class="nv-style-heading">Prologue</p>')
  expect(stored.html).toContain('<ul><li>A bullet from the real project.</li></ul>')
  expect(stored.html).toContain('<ol><li>A numbered item from the real project.</li></ol>')
  expect(stored.html).toContain('“<span style="font-weight:bold">Lorem ipsum!</span>”')
  expect(stored.html).toContain('Volupta’s aliqua—dolores esse…')
  expect(stored.html).toContain('font-weight:bold')
  expect(stored.html).toContain('font-style:italic')
  expect(stored.html).not.toContain("'93")
  expect(stored.html).not.toContain('$Scr_')

  await app.close()
})

const OLD_DRAFTS_FIXTURE = join(
  process.cwd(),
  '..',
  'tests',
  'Fixtures',
  'Scrivener',
  'OldDrafts.scriv'
)

/**
 * The bug the mapping exists for, driven through the dialog rather than the RPC.
 *
 * A writer about to start a fresh draft has an empty draft folder and nine
 * finished drafts filed under "Old". Nothing in the binder says those are
 * drafts, so the whole project arrived as research with no way to say otherwise.
 * The backend being able to do better is not the fix - the writer being able to
 * ask for it is, so this clicks the real dropdown in the real dialog.
 */
test('a binder whose drafts are only drafts by convention can be told so', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-imp-map-'))
  const root = join(workDir, 'OldDrafts.scriv')
  cpSync(OLD_DRAFTS_FIXTURE, root, { recursive: true })

  const { app, page } = await launch(workDir)
  await newProject(page, workDir)

  // The folder picker is the OS's, so it is answered for it in the main process
  // - the renderer's bridge is frozen and cannot be stubbed from the page, and
  // stubbing it there would skip the IPC this actually goes through. Everything
  // after this point is the dialog doing its own work.
  await app.evaluate(async ({ dialog }, picked: string) => {
    dialog.showOpenDialog = () =>
      Promise.resolve({ canceled: false, filePaths: [picked] }) as never
  }, root)

  await page.evaluate(() => window.novalistStores.shell.getState().openDialog('importManuscript'))
  await page.locator('.import-manuscript-dialog').waitFor()
  await page.getByRole('button', { name: /Scrivener project/ }).click()

  const rows = page.locator('.import-mapping-row')
  await expect(rows.first()).toBeVisible({ timeout: 30_000 })

  // What the rules made of it, which is the bug stated on screen.
  const old = page.getByRole('combobox', { name: 'Old', exact: true })
  await expect(old).toHaveValue('research')
  const draftSix = page.getByRole('combobox', { name: 'Old Draft 6- Started 10/30/2025?' })
  await expect(draftSix).toHaveValue('research')

  // Setting the folder sets everything in it, so a folder of nine drafts is
  // one action rather than nine.
  await old.selectOption('draft')
  await expect(draftSix).toHaveValue('draft')
  await expect(
    page.getByRole('combobox', { name: 'Old Draft 1- Started 04/2021' })
  ).toHaveValue('draft')

  // And a row inside it can still be put somewhere else afterwards.
  const oldNotes = page.getByRole('combobox', { name: 'Old Notes' })
  await expect(oldNotes).toHaveValue('draft')
  await oldNotes.selectOption('research')
  await expect(draftSix).toHaveValue('draft')

  // The plan below the rows follows the choices, before anything is written:
  // every draft is named, including the four that never got chapter folders.
  await expect(
    page.getByText('New draft: Old Draft 6- Started 10/30/2025?')
  ).toBeVisible({ timeout: 15_000 })
  await expect(
    page.getByText('New draft: Old Draft 9- Started 6/13/2026')
  ).toBeVisible()

  await page.getByRole('button', { name: 'Import', exact: true }).click()
  await expect(page.getByText(/Created 10 drafts/)).toBeVisible({ timeout: 120_000 })

  const drafts = (await page.evaluate(() =>
    window.novalistRpc.request('project/drafts')
  )) as { id: string; name: string; isActive: boolean }[]

  const imported = drafts.find((d) => d.name === 'Old Draft 6- Started 10/30/2025?')
  expect(imported).toBeTruthy()
  // The writer stayed on the draft they were about to start.
  expect(imported!.isActive).toBe(false)
  // All nine drafts, plus "Deleted Scenes" which came along with the folder and
  // was not put back - the cascade is a starting point, not a decision.
  expect(drafts.filter((d) => d.name.startsWith('Old Draft ')).length).toBe(9)

  const chapters = (await page.evaluate(async (draftId: string) => {
    await window.novalistRpc.request('project/switchDraft', [draftId])
    const state = (await window.novalistRpc.request('project/getState')) as {
      chapters: { title: string; scenes: unknown[] }[]
    }
    return state.chapters.map((c) => c.title)
  }, imported!.id)) as string[]

  expect(chapters).toEqual([
    'In The Beginning...',
    'Chapter 1: Signal',
    'Chapter 2: Hunt',
    'Chapter 3: Recruit',
    'Chapter 4: Ambush',
    'Chapter 5: Contact'
  ])

  await app.close()
})
