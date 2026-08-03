import { test, expect } from '@playwright/test'
import { launchApp, seedBook } from './harness'

/**
 * The things that get somebody's work back.
 *
 * The audit's own words for this cluster are that the safety net was per-scene
 * where it needed to be per-project. Snapshots, whole-project backups with
 * in-app restore, and research history are all rows whose only real test is
 * that the old version actually comes back - a list of restore points nobody
 * has restored from is a list, not a safety net.
 */

test('a snapshot restores the prose that was overwritten', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-safe-snap-')
  const book = await seedBook(h, { One: ['A'] })
  const { guid } = book.chapters[0]
  const sceneId = book.chapters[0].scenes[0].id

  await h.rpc('scenes/write', [guid, sceneId, '<p>The right words.</p>', 'The right words.'])
  await h.rpc('snapshots/take', [guid, sceneId, 'before the rewrite'])
  await h.rpc('scenes/write', [guid, sceneId, '<p>The wrong words.</p>', 'The wrong words.'])

  const list = await h.rpc<{ id: string; label: string }[]>('snapshots/list', [guid, sceneId])
  expect(list.map((s) => s.label), 'the snapshot was not taken').toContain('before the rewrite')

  await h.rpc('snapshots/restore', [guid, sceneId, list[0].id])

  const after = await h.rpc<{ html: string }>('scenes/read', [guid, sceneId])
  expect(after.html, 'the restore did not bring the prose back').toContain('The right words')

  await h.close()
})

test('two snapshots of one scene can be compared', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-safe-diff-')
  const book = await seedBook(h, { One: ['A'] })
  const { guid } = book.chapters[0]
  const sceneId = book.chapters[0].scenes[0].id

  await h.rpc('scenes/write', [guid, sceneId, '<p>Before.</p>', 'Before.'])
  await h.rpc('snapshots/take', [guid, sceneId, 'first'])
  await h.rpc('scenes/write', [guid, sceneId, '<p>After.</p>', 'After.'])
  await h.rpc('snapshots/take', [guid, sceneId, 'second'])

  const list = await h.rpc<{ id: string; label: string }[]>('snapshots/list', [guid, sceneId])
  expect(list.length).toBe(2)

  const diff = JSON.stringify(await h.rpc('snapshots/diff', [guid, sceneId, list[1].id, list[0].id]))
  // A comparison that cannot see the word that changed is a comparison nobody
  // can act on.
  expect(diff).toContain('Before')
  expect(diff).toContain('After')

  await h.close()
})

test('a whole-project backup is made and restored from inside the app', async () => {
  test.setTimeout(240_000)
  const h = await launchApp('nl-safe-backup-')
  const book = await seedBook(h, { One: ['A'] })
  const { guid } = book.chapters[0]
  const sceneId = book.chapters[0].scenes[0].id

  await h.rpc('scenes/write', [guid, sceneId, '<p>The original.</p>', 'The original.'])
  await h.rpc('backup/createMilestone', ['before the experiment'])

  const backups = await h.rpc<{ id: string; name: string; isMilestone: boolean }[]>('backup/list')
  // By name, not by position: the project may already have an automatic
  // backup from opening, and restoring that one would prove nothing.
  const milestone = backups.find((b) => b.name === 'before the experiment')
  expect(milestone, 'the milestone backup was not written').toBeTruthy()

  // Wreck the scene, then take the whole project back.
  await h.rpc('scenes/write', [guid, sceneId, '<p>Ruined.</p>', 'Ruined.'])
  expect(await h.rpc<boolean>('backup/restore', [milestone!.id]), 'restore reported failure')
    .toBe(true)

  const after = await h.rpc<{ html: string }>('scenes/read', [guid, sceneId])
  expect(after.html, 'the restore did not return the project').toContain('The original')

  await h.close()
})

test('research is saved, listed, and linked to the entry it is about', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-safe-research-')
  await seedBook(h, { One: ['A'] })

  const mira = await h.rpc<{ id: string }>('entities/create', ['character', 'Mira'])
  await h.rpc('research/save', [
    null, 'Harbour survey 1755', 'Note', 'The pier was rebuilt.', ['harbour'], [mira.id]
  ])

  const list = await h.rpc<{ id: string; title: string; tags: string[]; entityRefs: string[] }[]>(
    'research/list'
  )
  const item = list.find((r) => r.title === 'Harbour survey 1755')
  expect(item, 'the note was not saved').toBeTruthy()
  expect(item!.tags).toContain('harbour')
  // The link to the entry is what puts it on the Wiki article and in the
  // Inspector beside the scene.
  expect(item!.entityRefs, 'the note is about nobody').toContain(mira.id)

  await h.close()
})

test('cut prose is kept rather than lost with the scene', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-safe-darlings-')
  await seedBook(h, { One: ['A'] })

  await h.rpc('darlings/keep', ['The harbour swallowed the light.', 'One / A', 'too purple'])

  const list = await h.rpc<{ id: string; text: string; note?: string }[]>('darlings/list')
  expect(list.map((d) => d.text), 'the cut text was not kept')
    .toContain('The harbour swallowed the light.')

  await h.rpc('darlings/remove', [list[0].id])
  expect((await h.rpc<unknown[]>('darlings/list')).length).toBe(0)

  await h.close()
})
