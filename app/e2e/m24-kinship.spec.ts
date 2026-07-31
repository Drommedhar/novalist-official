import { test, expect, _electron as electron } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * How these two are related, derived rather than recorded.
 *
 * Novalist stores a tie as a role and a target - "mother", "Mira" - and could
 * always draw the lines. Nothing could say what they added up to, so a writer
 * looking at three connected boxes still could not tell that one of them was a
 * great-aunt. The words are the interface's and the arithmetic is the
 * backend's, which is what this goes through.
 */
test('centring the graph on somebody names how everyone else is related to them', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-kin-'))
  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  // Gran has two children; one of each has a child of their own. Mira and
  // Cousin are therefore first cousins, and Gran is Mira's grandmother -
  // neither of which is written down anywhere.
  const ids = await evaluateWhenReady(page, async (parent) => {
    const rpc = window.novalistRpc
    const state = await rpc.request('project/create', [parent, 'Kin', 'Book One'])
    window.novalistStores.project.getState().applyState(state as never)

    const make = async (name: string): Promise<string> =>
      ((await rpc.request('entities/create', ['character', name])) as { id: string }).id

    const gran = await make('Gran')
    const mum = await make('Mum')
    const aunt = await make('Aunt')
    const mira = await make('Mira')
    const cousin = await make('Cousin')

    // A row names what the target is to the subject: on Mum, "Mother -> Gran"
    // means Gran is Mum's mother. setRelationships writes the whole list, so
    // everything one entry says goes in a single call.
    const ties = async (id: string, targets: string[]): Promise<void> => {
      await rpc.request('entities/setRelationships', [
        id,
        targets.map((target) => ({ role: 'Mother', target, inverseRole: '' })),
        'character'
      ])
    }
    await ties(mum, ['Gran'])
    await ties(aunt, ['Gran'])
    await ties(mira, ['Mum'])
    await ties(cousin, ['Aunt'])

    return { gran, mira, cousin }
  }, workDir)
  await expect(page.locator('.activity-bar')).toBeVisible({ timeout: 30_000 })

  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('relationships'))
  await expect
    .poll(() => page.locator('.relationships-node').count(), { timeout: 20_000 })
    .toBeGreaterThan(0)

  // Nothing is related to anybody until the graph is centred on someone.
  await expect(page.locator('.relationships-kinship')).toHaveCount(0)

  // Centre on Mira through the picker the writer uses, and reach far enough for
  // the cousin - four hops away through Mum, Gran and Aunt.
  await page.locator('.relationships-root').selectOption(ids.mira)
  // The depth control only exists once a root is chosen, so wait for it rather
  // than indexing into the row and hoping it has rendered.
  const depth = page.locator('.relationships-depth')
  await expect(depth).toBeVisible({ timeout: 15_000 })
  await depth.selectOption('4')

  const labels = page.locator('.relationships-kinship')
  // Polled on the answer rather than on a count: the graph fills in as it
  // widens, and two labels are present long before the cousin is reachable.
  await expect
    .poll(async () => (await labels.allTextContents()).join(' | '), { timeout: 20_000 })
    .toMatch(/first cousin|Cousin oder Cousine|堂表亲/)

  // Gran is two generations up - "grandparent", not "2x great-grandparent",
  // which is what a plural rule produces and what makes the whole thing
  // untrustworthy.
  const texts = (await labels.allTextContents()).join(' | ')
  expect(texts).toMatch(/grandparent|Grosselternteil|祖父母/)
  expect(texts).not.toMatch(/2x great/)

  await app.close()
})
