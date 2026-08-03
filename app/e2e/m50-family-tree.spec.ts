import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * A family tree shows the family, not the writer's line of descent.
 *
 * The walk went strictly up from the person it was centred on and then strictly
 * down, so it could only ever reach the people they descend from and the people
 * who descend from them. Every branch was missing: no brothers, no aunts, no
 * cousins, no nieces. What a tree has to do is go up the line and then come
 * back down from every ancestor it found.
 *
 * Reported from the checklist as "Not showing all members...".
 */
test('the family tree shows the branches, not only the line through the root', async () => {
  test.setTimeout(180_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-tree-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({
    args: ['out/main/index.js', `--user-data-dir=${join(workDir, 'profile')}`],
    env
  })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  // Gran has two daughters. Mira descends from one and Cousin from the other,
  // so Aunt and Cousin are family and neither is on the line through Mira.
  // Tom is named as Mira's brother with no parents recorded anywhere, which is
  // how a writer who has not built the family out puts a sibling down.
  const ids = await evaluateWhenReady(page, async (parent) => {
    const rpc = window.novalistRpc
    const state = await rpc.request('project/create', [parent, 'Tree', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)

    const make = async (name: string): Promise<string> =>
      ((await rpc.request('entities/create', ['character', name])) as { id: string }).id

    const gran = await make('Gran')
    const mum = await make('Mum')
    const aunt = await make('Aunt')
    const mira = await make('Mira')
    const cousin = await make('Cousin')
    const tom = await make('Tom')
    const kid = await make('Kid')

    const ties = async (
      id: string,
      rows: { role: string; target: string }[]
    ): Promise<void> => {
      await rpc.request('entities/setRelationships', [
        id,
        rows.map((r) => ({ role: r.role, target: r.target, inverseRole: '' })),
        'character'
      ])
    }
    await ties(mum, [{ role: 'Mother', target: 'Gran' }])
    await ties(aunt, [{ role: 'Mother', target: 'Gran' }])
    await ties(mira, [{ role: 'Mother', target: 'Mum' }, { role: 'Brother', target: 'Tom' }])
    await ties(cousin, [{ role: 'Mother', target: 'Aunt' }])
    await ties(kid, [{ role: 'Mother', target: 'Mira' }])

    return { mira, gran, aunt, cousin, tom, kid }
  }, workDir)
  await expect(page.locator('.activity-bar')).toBeVisible({ timeout: 30_000 })

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('relationships'))
  await expect
    .poll(() => page.locator('.relationships-node').count(), { timeout: 20_000 })
    .toBeGreaterThan(0)

  await page.locator('.relationships-root').selectOption(ids.mira)
  await page.getByRole('button', { name: /As tree|Als Stammbaum|树状图/ }).click()

  const names = async (): Promise<string[]> =>
    (await page.locator('.tree-node .tree-name').allTextContents()).map((s) => s.trim()).sort()

  await expect.poll(names, { timeout: 20_000 }).toContain('Mira')

  // The whole family, at the default three generations either way.
  expect(await names(), 'the tree is missing branches of the family').toEqual([
    'Aunt',
    'Cousin',
    'Gran',
    'Kid',
    'Mira',
    'Mum',
    'Tom'
  ])

  await app.close()
})

/**
 * The generation controls still bound the tree.
 *
 * Reaching the branches must not mean reaching everybody: asking for one
 * generation up should still stop at the parents, and the aunt a generation
 * above them should stay out.
 */
test('the generation controls still bound how far the tree reaches', async () => {
  test.setTimeout(180_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-tree-depth-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({
    args: ['out/main/index.js', `--user-data-dir=${join(workDir, 'profile')}`],
    env
  })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  const ids = await evaluateWhenReady(page, async (parent) => {
    const rpc = window.novalistRpc
    const state = await rpc.request('project/create', [parent, 'Tree', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)

    const make = async (name: string): Promise<string> =>
      ((await rpc.request('entities/create', ['character', name])) as { id: string }).id

    await make('Gran')
    const mum = await make('Mum')
    const aunt = await make('Aunt')
    const mira = await make('Mira')
    const cousin = await make('Cousin')

    const ties = async (id: string, target: string): Promise<void> => {
      await rpc.request('entities/setRelationships', [
        id,
        [{ role: 'Mother', target, inverseRole: '' }],
        'character'
      ])
    }
    await ties(mum, 'Gran')
    await ties(aunt, 'Gran')
    await ties(mira, 'Mum')
    await ties(cousin, 'Aunt')

    return { mira }
  }, workDir)
  await expect(page.locator('.activity-bar')).toBeVisible({ timeout: 30_000 })

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('relationships'))
  await expect
    .poll(() => page.locator('.relationships-node').count(), { timeout: 20_000 })
    .toBeGreaterThan(0)

  await page.locator('.relationships-root').selectOption(ids.mira)
  await page.getByRole('button', { name: /As tree|Als Stammbaum|树状图/ }).click()

  const names = async (): Promise<string[]> =>
    (await page.locator('.tree-node .tree-name').allTextContents()).map((s) => s.trim()).sort()

  await expect.poll(names, { timeout: 20_000 }).toContain('Cousin')

  // One generation up reaches Mum and stops. Gran is out, and so is everyone
  // who is only family through her.
  const depths = page.locator('.relationships-field .relationships-depth')
  await expect(depths).toHaveCount(2)
  await depths.first().selectOption('1')

  await expect.poll(names, { timeout: 15_000 }).toEqual(['Mira', 'Mum'])

  await app.close()
})

/**
 * The graph's reach control is not offered in tree view.
 *
 * It fetches the neighbourhood the graph draws; the tree is built from every
 * entry and reaches as far as its own two generation controls say. Leaving it
 * up put three depth dropdowns side by side, one of which did nothing.
 */
test('tree view offers its own two depth controls and not the graph reach', async () => {
  test.setTimeout(180_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-tree-controls-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({
    args: ['out/main/index.js', `--user-data-dir=${join(workDir, 'profile')}`],
    env
  })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  const ids = await evaluateWhenReady(page, async (parent) => {
    const rpc = window.novalistRpc
    const state = await rpc.request('project/create', [parent, 'Tree', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)
    const mira = ((await rpc.request('entities/create', ['character', 'Mira'])) as { id: string })
      .id
    return { mira }
  }, workDir)
  await expect(page.locator('.activity-bar')).toBeVisible({ timeout: 30_000 })

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('relationships'))
  await expect
    .poll(() => page.locator('.relationships-node').count(), { timeout: 20_000 })
    .toBeGreaterThan(0)

  await page.locator('.relationships-root').selectOption(ids.mira)

  // Graph view: one reach control, and it says what it is.
  await expect(page.locator('.relationships-depth')).toHaveCount(1)
  const graphField = page.locator('.relationships-field')
  await expect(graphField).toHaveCount(1)
  expect((await graphField.innerText()).trim().length).toBeGreaterThan(0)

  await page.getByRole('button', { name: /As tree|Als Stammbaum|树状图/ }).click()

  // Tree view: two generation controls, and the graph's reach is gone rather
  // than sitting there doing nothing.
  await expect(page.locator('.relationships-depth')).toHaveCount(2)
  const fields = page.locator('.relationships-field')
  await expect(fields).toHaveCount(2)
  for (const text of await fields.allInnerTexts()) {
    expect(text.trim().length, 'a depth dropdown is unlabelled').toBeGreaterThan(0)
  }

  await app.close()
})
