import { test, expect } from '@playwright/test'
import { launchApp, seedBook, shapeOf, state, type Book } from './harness'

/**
 * The structural verbs the audit calls out as the gap in the middle of the
 * product: the data model was strong and the operations on it were missing.
 * Insert-at-position with renumber, a real trash with restore, archive and
 * restore, moving scenes between chapters, and collections as curated sets.
 *
 * Each of these rewrites the binder, which is the one place where a wrong
 * answer loses somebody's work rather than just looking wrong.
 */

test('a chapter inserts at a position and pushes the rest down', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-struct-insert-')
  await seedBook(h, { 'One': ['A'], 'Three': ['C'] })

  // One-based, which is what the binder's "insert before/after" passes.
  await h.rpc('project/createChapter', ['Two', 2])

  expect(Object.keys(shapeOf(await state(h)))).toEqual(['One', 'Two', 'Three'])
  await h.close()
})

test('a deleted chapter goes to the trash and comes back whole', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-struct-trash-')
  const book = await seedBook(h, { 'Keep': ['A'], 'Drop': ['B', 'C'] })
  const drop = book.chapters.find((c) => c.title === 'Drop')!

  await h.rpc('project/deleteChapter', [drop.guid])
  expect(Object.keys(shapeOf(await state(h))), 'delete did not remove it').toEqual(['Keep'])

  const trashed = await h.rpc<{ guid: string; title: string }[]>('project/trashedChapters')
  expect(trashed.map((c) => c.title), 'it never reached the trash').toContain('Drop')

  await h.rpc('project/restoreChapter', [drop.guid])
  // Its scenes come back with it: a restore that returns an empty chapter is
  // the same loss with a chapter heading on top.
  expect(shapeOf(await state(h))).toEqual({ Keep: ['A'], Drop: ['B', 'C'] })

  await h.close()
})

test('an archived scene leaves the binder and can be put back', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-struct-archive-')
  const book = await seedBook(h, { 'One': ['Keep', 'Park'] })
  const chapter = book.chapters[0]
  const park = chapter.scenes.find((s) => s.title === 'Park')!

  // Archiving is a bulk operation, which is how the binder does it.
  await h.rpc('sceneBulk/archive', [[park.id]])

  expect(shapeOf(await state(h))['One'], 'the scene stayed in the binder').toEqual(['Keep'])
  const archived = await h.rpc<{ title: string }[]>('scenes/archived')
  expect(archived.map((s) => s.title), 'the scene is archived nowhere').toContain('Park')

  await h.rpc('scenes/restoreArchived', [park.id, chapter.guid])
  expect(shapeOf(await state(h))['One'], 'restore did not put it back').toContain('Park')
  await h.close()
})

test('scenes move to another chapter and keep their order', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-struct-move-')
  const book = await seedBook(h, { 'One': ['A', 'B'], 'Two': ['C'] })
  const [one, two] = book.chapters
  const ids = one.scenes.map((s) => s.id)

  await h.rpc('project/moveScenes', [ids, two.guid, 0])

  const after = shapeOf(await state(h))
  expect(after['One'], 'the scenes did not leave').toEqual([])
  expect(after['Two'], 'the scenes did not arrive in order at the front').toEqual(['A', 'B', 'C'])

  await h.close()
})

test('a collection is a named, reorderable set that survives a reload of the state', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-struct-coll-')
  const book = await seedBook(h, { 'One': ['A', 'B', 'C'] })
  const scenes = book.chapters[0].scenes

  type Coll = { id: string; name: string; scenes: { sceneId: string }[] }
  const find = async (from?: Coll[]): Promise<Coll> => {
    const list = from ?? (await h.rpc<Coll[]>('collections/list'))
    const mine = list.find((c) => c.name === 'Mira thread')
    expect(mine, 'the collection was not listed after being created').toBeTruthy()
    return mine!
  }

  // create returns the whole list, the way the panel repaints from it.
  const created = await find(
    await h.rpc<Coll[]>('collections/create', ['Mira thread', [scenes[0].id]])
  )
  await h.rpc('collections/add', [created.id, [scenes[2].id]])

  const mine = await find()
  expect(mine.scenes.map((s) => s.sceneId), 'the collection is not the scenes put in it')
    .toEqual([scenes[0].id, scenes[2].id])

  // The plan claims named, reorderable collections, so exercise those verbs
  // through the shipped panel rather than only proving the storage methods.
  await h.page.getByRole('button', { name: 'Collections', exact: true }).click()
  await h.page.getByRole('button', { name: 'Rename collection' }).click()
  const rename = h.page.getByRole('textbox', { name: 'Rename collection' })
  await rename.fill('Tuesday fixes')
  await h.page.getByRole('button', { name: 'Save collection name' }).click()
  await expect(h.page.getByText('Tuesday fixes', { exact: true })).toBeVisible()

  await h.page.getByRole('button', { name: 'Move earlier in collection' }).nth(1).click()
  // The button starts an RPC and repaints from its result. Wait on the stored
  // order rather than racing a second RPC against the first one.
  await expect
    .poll(async () => {
      const renamed = (await h.rpc<Coll[]>('collections/list')).find(
        (c) => c.name === 'Tuesday fixes'
      )
      return renamed?.scenes.map((s) => s.sceneId)
    })
    .toEqual([scenes[2].id, scenes[0].id])

  const renamed = (await h.rpc<Coll[]>('collections/list')).find(
    (c) => c.name === 'Tuesday fixes'
  )!

  await h.rpc('collections/remove', [renamed.id, scenes[0].id])
  expect((await h.rpc<Coll[]>('collections/list'))[0].scenes.map((s) => s.sceneId)).toEqual([
    scenes[2].id
  ])

  await h.close()
})

test('word targets are kept for a scene, a chapter and an act alike', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-struct-targets-')
  const book = await seedBook(h, { 'One': ['A'] })
  const chapter = book.chapters[0]

  await h.rpc('project/setChapterAct', [chapter.guid, 'Act One'])
  await h.rpc('targets/setScene', [chapter.guid, chapter.scenes[0].id, 500])
  await h.rpc('targets/setChapter', [chapter.guid, 2000])
  await h.rpc('targets/setAct', ['Act One', 8000])

  const all = JSON.stringify(await h.rpc('targets/all'))
  // All three levels, which is the row: a chapter-only target cannot say what
  // an act is for.
  expect(all).toContain('500')
  expect(all).toContain('2000')
  expect(all).toContain('8000')

  await h.close()
})

test('a scene stage is set from the writer’s own vocabulary', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-struct-stage-')
  const book = await seedBook(h, { 'One': ['A'] })
  const chapter = book.chapters[0]

  const stages = await h.rpc<{ key: string; label: string }[]>('stages/list')
  expect(stages.length, 'a project starts with no stages at all').toBeGreaterThan(0)

  await h.rpc('stages/setSceneStage', [chapter.guid, chapter.scenes[0].id, stages[1].key])

  const after = await state(h) as Book & {
    chapters: { scenes: { stage?: string | null }[] }[]
  }
  expect(after.chapters[0].scenes[0].stage, 'the stage did not stick').toBe(stages[1].key)

  const breakdown = JSON.stringify(await h.rpc('stages/breakdown'))
  expect(breakdown, 'the dashboard breakdown does not see the stage').toContain(stages[1].key)

  await h.close()
})
