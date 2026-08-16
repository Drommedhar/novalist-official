import { test, expect } from '@playwright/test'
import { rmSync } from 'node:fs'
import { join } from 'node:path'
import { launchApp, type Harness } from './harness'

/**
 * The recent-projects list only offers projects that are still there.
 *
 * A project deleted outside Novalist - which is how projects actually go away -
 * stayed on the welcome screen and in the File menu forever, and clicking it
 * failed. The list is a set of ways back into work, so a row that cannot be
 * opened does not belong on it.
 *
 * Driven through the store the welcome screen and the File menu both read, and
 * asserted on the rendered cards, because the entry being absent from the RPC
 * is only half of it.
 */

type Recent = { name: string; path: string }

async function makeProject(h: Harness, name: string): Promise<string> {
  await h.rpc('project/create', [h.workDir, name, 'Book One'])
  const state = await h.rpc<{ projectPath: string }>('project/getState')
  return state.projectPath
}

const recents = (h: Harness): Promise<Recent[]> => h.rpc<Recent[]>('project/recent')

test('a project whose folder is gone drops off the recents list', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-recents-')

  await makeProject(h, 'Still Here')
  const doomed = await makeProject(h, 'Deleted Later')

  expect((await recents(h)).map((r) => r.name).sort()).toEqual(['Deleted Later', 'Still Here'])

  // Closed first so nothing holds the folder open, then deleted the way a
  // writer would: in the file manager, with Novalist none the wiser.
  await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())
  rmSync(doomed, { recursive: true, force: true })

  expect((await recents(h)).map((r) => r.name)).toEqual(['Still Here'])

  // And the welcome screen shows what the list says.
  await h.page.evaluate(() => window.novalistStores.project.getState().loadRecents())
  await expect(h.page.locator('.start-recent-card')).toHaveCount(1)
  await expect(h.page.locator('.start-recent-name')).toHaveText('Still Here')
  await expect(h.page.getByText('Deleted Later')).toHaveCount(0)

  await h.close()
})

test('a folder that is no longer a project drops off too', async () => {
  test.setTimeout(180_000)
  const h = await launchApp('nl-recents-gutted-')

  const root = await makeProject(h, 'Gutted')
  await h.page.evaluate(() => window.novalistStores.project.getState().closeProject())

  // The folder survives; what made it a project does not.
  rmSync(join(root, '.novalist'), { recursive: true, force: true })

  expect(await recents(h)).toEqual([])

  await h.close()
})
