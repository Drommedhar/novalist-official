import { test, expect } from '@playwright/test'
import { launchApp, seedBook, state } from './harness'

/**
 * The per-scene record the Inspector writes.
 *
 * These rows are the answer to a cluster of audit findings that all said the
 * same thing from different directions: scenes were a closed field set, so a
 * writer tracking tension, a POV, a cast or a promise had to overload tags and
 * nothing downstream could key off any of it. Each one is only worth anything
 * if the value survives the round trip and comes back on the scene the writer
 * put it on.
 */

type SceneMeta = Record<string, unknown>

test('goal, outcome, synopsis, notes and POV all stay on the scene', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-insp-fields-')
  const book = await seedBook(h, { One: ['A'] })
  const { guid } = book.chapters[0]
  const sceneId = book.chapters[0].scenes[0].id

  await h.rpc('scenes/setGoalOutcome', [guid, sceneId, 'Reach the harbour', 'Misses the boat'])
  await h.rpc('scenes/setSynopsis', [guid, sceneId, 'She arrives too late.'])
  await h.rpc('scenes/setNotes', [guid, sceneId, 'Check the tide table.'])
  await h.rpc('scenes/setPov', [guid, sceneId, 'Mira'])

  const meta = JSON.stringify(await h.rpc<SceneMeta>('scenes/getMeta', [guid, sceneId]))
  // POV is an analysis override rather than a plain field - it is derived per
  // scene and the writer's answer overrides the derivation - so it comes back
  // through getSceneEdit, which is the call the Inspector's own fields make.
  const edit = JSON.stringify(await h.rpc('project/getSceneEdit', [guid, sceneId]))
  expect(meta, 'the goal was lost').toContain('Reach the harbour')
  expect(meta, 'the outcome was lost').toContain('Misses the boat')
  expect(meta, 'the synopsis was lost').toContain('She arrives too late.')
  expect(meta, 'the notes were lost').toContain('Check the tide table.')
  expect(edit, 'the POV the writer set never came back to the Inspector').toContain('Mira')

  await h.close()
})

test('a scene cast is asserted, not inferred from the prose', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-insp-cast-')
  const book = await seedBook(h, { One: ['A'] })
  const { guid } = book.chapters[0]
  const sceneId = book.chapters[0].scenes[0].id

  const mira = await h.rpc<{ id: string }>('entities/create', ['character', 'Mira'])
  // Nothing in the prose names her: the point of the row is that presence can
  // be stated rather than detected.
  await h.rpc('scenes/write', [guid, sceneId, '<p>Nobody in particular.</p>', 'Nobody in particular.'])
  await h.rpc('scenes/setCast', [guid, sceneId, [mira.id], null])

  const meta = JSON.stringify(await h.rpc<SceneMeta>('scenes/getMeta', [guid, sceneId]))
  expect(meta, 'the asserted cast did not stick').toContain(mira.id)

  await h.close()
})

test('a custom scene property becomes a typed field with a value', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-insp-props-')
  const book = await seedBook(h, { One: ['A'] })
  const { guid } = book.chapters[0]
  const sceneId = book.chapters[0].scenes[0].id

  await h.rpc('manuscriptProps/setDefinitions', [[
    { key: 'tension', label: 'Tension', type: 'Enum', enumOptions: ['Low', 'High'], scope: 'Scene' },
    { key: 'sourceNote', label: 'Source', type: 'String', scope: 'Chapter' }
  ]])

  const defs = await h.rpc<{ key: string; scope: string }[]>('manuscriptProps/definitions')
  expect(defs.map((d) => d.key), 'the definitions were not saved').toEqual(
    expect.arrayContaining(['tension', 'sourceNote'])
  )

  await h.rpc('manuscriptProps/setSceneValue', [sceneId, 'tension', 'High'])
  await h.rpc('manuscriptProps/setChapterValue', [guid, 'sourceNote', 'From the 1755 survey'])

  const sceneValues = JSON.stringify(await h.rpc('manuscriptProps/allSceneValues'))
  expect(sceneValues, 'the scene value did not stick').toContain('High')

  const chapterValues = JSON.stringify(await h.rpc('manuscriptProps/chapterValues', [guid]))
  expect(chapterValues, 'the chapter value did not stick').toContain('From the 1755 survey')

  await h.close()
})

test('a link between two scenes is visible from both ends', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-insp-links-')
  const book = await seedBook(h, { One: ['A', 'B'] })
  const { guid, scenes } = book.chapters[0]

  await h.rpc('links/add', [guid, scenes[0].id, 'scene', scenes[1].id, 'Pays off here'])

  const forward = JSON.stringify(await h.rpc('links/list', [guid, scenes[0].id]))
  expect(forward, 'the link is missing from the scene that owns it').toContain(scenes[1].id)

  // The other end is the half that makes it a link rather than a note.
  const back = JSON.stringify(await h.rpc('links/backlinks', ['scene', scenes[1].id]))
  expect(back, 'the target cannot see what points at it').toContain(scenes[0].id)

  await h.close()
})

test('a scene held out of the book stays in the plan', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-insp-inactive-')
  const book = await seedBook(h, { One: ['A', 'B'] })
  const { guid, scenes } = book.chapters[0]

  await h.rpc('scenes/setInactive', [guid, scenes[1].id, true])

  const after = await state(h)
  const titles = after.chapters[0].scenes.map((s) => s.title)
  // Inactive is not archived: the scene stays in the binder and the corkboard.
  expect(titles, 'an inactive scene left the binder').toEqual(['A', 'B'])

  const meta = JSON.stringify(await h.rpc<SceneMeta>('scenes/getMeta', [guid, scenes[1].id]))
  expect(meta).toContain('"inactive":true')

  await h.close()
})

test('a comment can be made a to-do and given a verdict', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-insp-inbox-')
  const book = await seedBook(h, { One: ['A'] })
  const { guid } = book.chapters[0]
  const sceneId = book.chapters[0].scenes[0].id

  await h.rpc('scenes/setAnnotations', [
    guid, sceneId,
    [{ id: 'c1', anchor: 'harbour', text: 'Is the tide right here?', author: 'A reader' }],
    []
  ])

  const listed = await h.rpc<{ commentId: string; text: string }[]>('inbox/list', [false])
  expect(listed.map((c) => c.text), 'the comment never reached the inbox')
    .toContain('Is the tide right here?')

  await h.rpc('inbox/setTodo', [sceneId, 'c1', true])
  await h.rpc('inbox/setVerdict', [sceneId, 'c1', 'declined'])

  const after = JSON.stringify(await h.rpc('inbox/list', [true]))
  // Declining is a decision that keeps saying so, which is the row: resolving
  // alone could not tell agreement from disagreement. It also closes the note.
  expect(after, 'the verdict was not kept').toContain('declined')
  expect(after, 'a declined note stayed open').toContain('"resolved":true')

  await h.close()
})
