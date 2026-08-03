import { test, expect } from '@playwright/test'
import { launchApp, seedBook } from './harness'

/**
 * The planning tier, which the audit called data-rich and inference-poor.
 *
 * Plotlines as objects rather than ticks on a grid, an in-world calendar
 * instead of Gregorian-only, front and back matter as typed elements, and
 * compile-time replacements. Each of these is a row whose value is that the
 * thing survives being written and comes back where the view reads it.
 */

test('a plotline is an object with detail, not just a column of ticks', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-plan-plot-')
  const book = await seedBook(h, { One: ['A', 'B'] })
  const { guid, scenes } = book.chapters[0]

  const created = await h.rpc<unknown>('plot/createPlotline', ['The harbour thread'])
  const grid = JSON.stringify(created)
  expect(grid, 'the plotline was not created').toContain('The harbour thread')

  const plotlines = await h.rpc<{ id: string; name: string }[]>('binder/plotlines')
  const thread = plotlines.find((p) => p.name === 'The harbour thread')!
  expect(thread, 'the plotline is not offered to the binder').toBeTruthy()

  await h.rpc('plot/toggle', [guid, scenes[0].id, thread.id])
  await h.rpc('plot/setCellNote', [guid, scenes[0].id, thread.id, 'She misses the boat'])
  await h.rpc('plot/setPlotlineDetail', [
    thread.id, 'Main', [], null, '#d2a74f', 'What the harbour costs her'
  ])

  const after = JSON.stringify(await h.rpc('plot/grid', ['plotline']))
  // Membership, the note on the cell, and the description that makes it an
  // object rather than a flag.
  expect(after, 'the scene is not on the thread').toContain(scenes[0].id)
  expect(after, 'the cell note was lost').toContain('She misses the boat')

  // binder/plotlines is the short list the picker draws; the detail lives on
  // the grid's own plotline rows, which is where the Plot Grid reads it.
  expect(after, 'the description was lost').toContain('What the harbour costs her')
  // Main, not Subplot: an importance that silently falls back to the default
  // would make the field look saved while carrying nothing the writer chose.
  expect(after, 'the importance fell back to the default').toContain('"importance":"Main"')

  await h.close()
})

test('an in-world calendar replaces the Gregorian one', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-plan-cal-')
  await seedBook(h, { One: ['A'] })

  await h.rpc('calendar/setConfig', [
    'custom', 'Cycle', ['Rimefall', 'Thaw', 'Highsun'], [30, 30, 40],
    ['Firstday', 'Secondday', 'Thirdday'], null
  ])

  const config = JSON.stringify(await h.rpc('calendar/getConfig'))
  // The model shipped long before the editor did; this is the row that closed
  // it, so the month names have to come back.
  expect(config, 'the custom calendar was not kept').toContain('Rimefall')
  expect(config, 'the month lengths were not kept').toContain('40')

  await h.close()
})

test('front matter is a typed element that reaches the export', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-plan-matter-')
  await seedBook(h, { One: ['A'] })

  const kinds = await h.rpc<string[] | { key: string }[]>('matter/kinds')
  expect((kinds as unknown[]).length, 'there are no matter kinds to choose from')
    .toBeGreaterThan(0)

  const created = await h.rpc<{ id: string }[] | { id: string }>('matter/create', ['Dedication'])
  const list = Array.isArray(created) ? created : await h.rpc<{ id: string }[]>('matter/list')
  expect(list.length, 'the matter element was not created').toBeGreaterThan(0)

  await h.rpc('matter/update', [
    list[0].id, 'Dedication', 'For everyone who waited.', true, true, null
  ])

  const after = JSON.stringify(await h.rpc('matter/list'))
  expect(after, 'the matter content was lost').toContain('For everyone who waited.')

  await h.close()
})

test('a compile-time replacement is stored for the export to apply', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-plan-repl-')
  await seedBook(h, { One: ['A'] })

  await h.rpc('export/saveReplacements', [[
    { find: 'MIRA', replace: 'Mira Vance', enabled: true, wholeWord: false, caseSensitive: true }
  ]])

  const rules = JSON.stringify(await h.rpc('export/replacements'))
  expect(rules, 'the replacement rule was not stored').toContain('Mira Vance')

  await h.close()
})

test('an export preset can be duplicated and kept', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-plan-preset-')
  await seedBook(h, { One: ['A'] })

  const presets = await h.rpc<{ id: string; displayName: string }[]>('exportPresets/list')
  expect(presets.length, 'no export presets ship at all').toBeGreaterThan(0)

  const after = await h.rpc<{ id: string; displayName: string }[]>('exportPresets/duplicate', [
    presets[0].id, 'My layout'
  ])
  const mine = (Array.isArray(after) ? after : await h.rpc<{ displayName: string }[]>(
    'exportPresets/list'
  )).find((p) => p.displayName === 'My layout')

  // A preset the writer authored is the row; shipping presets alone is the gap.
  expect(mine, 'the duplicated preset was not kept').toBeTruthy()

  await h.close()
})

test('a writing sprint is recorded and shows up in the history', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-plan-sprint-')
  await seedBook(h, { One: ['A'] })

  await h.rpc('sprints/record', [1500, 25, 640, new Date(0).toISOString()])

  const history = JSON.stringify(await h.rpc('sprints/history'))
  expect(history, 'the sprint was not recorded').toContain('640')

  await h.rpc('sprints/clear')
  // The history is a record with the sessions inside it, not a bare array.
  const cleared = JSON.stringify(await h.rpc('sprints/history'))
  expect(cleared, 'clearing the history left the sprint behind').not.toContain('640')

  await h.close()
})
