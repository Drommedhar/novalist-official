import { test, expect, _electron as electron, type Page } from '@playwright/test'
import { mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { evaluateWhenReady } from './appReady'

/**
 * The Codex behaviours that reach outside their own record.
 *
 * These are the rows where the blast radius is widest: a cascade rename
 * rewrites prose in scene files, and per-chapter and per-scene overrides are
 * an inheritance chain that nothing else in the category models, so a wrong
 * answer is a character quietly described wrongly for one chapter. Unit tests
 * cover the services; what is checked here is that the app wires them together
 * and that the result comes back out through the same API the screens read.
 */

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

/** A project with one chapter, one scene, and a character mentioned in it. */
async function seed(page: Page, parent: string): Promise<{
  chapterGuid: string
  sceneId: string
  miraId: string
}> {
  return evaluateWhenReady(page, async (dir: string) => {
    const rpc = window.novalistRpc
    let state = await rpc.request('project/create', [dir, 'Codex', 'Book One'])
    state = await rpc.request('project/createChapter', ['Chapter One'])
    const chapters = (state as { chapters: { guid: string }[] }).chapters
    const chapterGuid = chapters[chapters.length - 1].guid
    state = await rpc.request('project/createScene', [chapterGuid, 'Arrival'])
    const scenes = (state as { chapters: { guid: string; scenes: { id: string }[] }[] }).chapters
      .find((c) => c.guid === chapterGuid)!.scenes
    const sceneId = scenes[0].id

    const mira = (await rpc.request('entities/create', ['character', 'Mira Vance'])) as { id: string }
    // Mentions carry the entity id, which is what a rename follows. Raw text
    // is deliberately left alone - replacing every matching string would
    // rewrite prose that merely uses the same words.
    const span = (t: string): string =>
      `<span class="nv-entity-mention" data-entity-id="${mira.id}">${t}</span>`
    await rpc.request('scenes/write', [
      chapterGuid, sceneId,
      `<p>${span('Mira Vance')} walked the harbour wall, and ${span('Mira Vance')} did not look back.</p>`,
      'Mira Vance walked the harbour wall, and Mira Vance did not look back.'
    ])
    window.novalistStores.project.getState().applyState(
      (await rpc.request('project/getState')) as never
    )
    return { chapterGuid, sceneId, miraId: mira.id }
  }, parent)
}

const sceneText = async (page: Page, chapterGuid: string, sceneId: string): Promise<string> => {
  const dto = (await page.evaluate(
    (a: { c: string; s: string }) => window.novalistRpc.request('scenes/read', [a.c, a.s]),
    { c: chapterGuid, s: sceneId }
  )) as { html: string }
  return dto.html
}

test('renaming a Codex entry rewrites the prose that names it', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-codex-rename-'))
  const { app, page } = await launch(workDir)
  const { chapterGuid, sceneId, miraId } = await seed(page, workDir)

  expect(await sceneText(page, chapterGuid, sceneId)).toContain('Mira Vance')

  await page.evaluate(
    (id: string) => window.novalistRpc.request('entities/update', [
      'character', id, { name: 'Mira Calder' }
    ]),
    miraId
  )

  const after = await sceneText(page, chapterGuid, sceneId)
  // Every occurrence, not just the first: the old reader replaced one and left
  // the rest to stop matching silently.
  expect(after, 'the old name survived the rename').not.toContain('Mira Vance')
  expect(after.match(/Mira Calder/g) ?? [], 'not every mention followed').toHaveLength(2)

  await app.close()
})

test('a chapter override restates a field for that chapter and nowhere else', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-codex-override-'))
  const { app, page } = await launch(workDir)
  const { chapterGuid, miraId } = await seed(page, workDir)

  const second = (await page.evaluate(async () => {
    const state = await window.novalistRpc.request('project/createChapter', ['Chapter Two'])
    const chapters = (state as { chapters: { guid: string }[] }).chapters
    return chapters[chapters.length - 1].guid
  })) as string

  await page.evaluate(
    (a: { id: string; c: string }) => window.novalistRpc.request('entities/update', [
      'character', a.id, { eyeColor: 'green' }
    ]),
    { id: miraId, c: chapterGuid }
  )
  await page.evaluate(
    (a: { id: string; c: string }) => window.novalistRpc.request('entities/setOverride', [
      a.id, a.c, null, { eyeColor: 'grey' }
    ]),
    { id: miraId, c: chapterGuid }
  )

  // Read back through the peek, which is what the editor's hover card and the
  // Inspector both call - so this asserts the value the writer actually sees.
  const resolve = (chapter: string): Promise<Record<string, unknown>> =>
    page.evaluate(
      (a: { id: string; c: string }) =>
        window.novalistRpc.request('entities/peek', ['character', a.id, a.c, null, null]),
      { id: miraId, c: chapter }
    ) as Promise<Record<string, unknown>>

  const inOne = JSON.stringify(await resolve(chapterGuid))
  const inTwo = JSON.stringify(await resolve(second))

  expect(inOne, 'the override did not apply in its own chapter').toContain('grey')
  // The point of an override is that it is local: a chapter that never set one
  // has to keep inheriting, or the feature is just an edit.
  expect(inTwo, 'the override leaked into a chapter that never set it').not.toContain('grey')
  expect(inTwo, 'the base value was lost').toContain('green')

  await app.close()
})

test('a relationship written on one entry appears on the other', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-codex-rel-'))
  const { app, page } = await launch(workDir)
  const { miraId } = await seed(page, workDir)

  const tomasId = (await page.evaluate(async () => {
    const t = (await window.novalistRpc.request('entities/create', ['character', 'Tomas Vance'])) as {
      id: string
    }
    return t.id
  })) as string

  await page.evaluate(
    (id: string) => window.novalistRpc.request('entities/setRelationships', [
      id, [{ role: 'Brother', target: 'Tomas Vance', inverseRole: 'Sister' }], 'character'
    ]),
    miraId
  )

  const tomas = JSON.stringify(
    await page.evaluate(
      (id: string) => window.novalistRpc.request('entities/get', ['character', id]),
      tomasId
    )
  )

  // The inverse is authored onto the target, so the graph agrees with itself
  // whichever entry you opened.
  expect(tomas, 'the inverse relationship never reached the other entry').toContain('Sister')
  expect(tomas, 'the inverse does not point back at the subject').toContain('Mira Vance')

  await app.close()
})

test('a custom entity type takes typed fields and keeps them', async () => {
  test.setTimeout(180_000)
  const workDir = mkdtempSync(join(tmpdir(), 'nl-codex-type-'))
  const { app, page } = await launch(workDir)
  await seed(page, workDir)

  await page.evaluate(() =>
    window.novalistRpc.request('entities/saveCustomType', [{
      typeKey: 'ship',
      displayName: 'Ship',
      displayNamePlural: 'Ships',
      defaultFields: [{ key: 'tonnage', displayName: 'Tonnage', type: 'Int' }]
    }])
  )

  const types = (await page.evaluate(() =>
    window.novalistRpc.request('entities/customTypes')
  )) as { typeKey: string }[]
  expect(types.map((t) => t.typeKey), 'the type was not created').toContain('ship')

  const ship = (await page.evaluate(() =>
    window.novalistRpc.request('entities/create', ['ship', 'The Kestrel'])
  )) as { id: string }
  await page.evaluate(
    (id: string) => window.novalistRpc.request('entities/update', [
      'ship', id, { tonnage: '420' }
    ]),
    ship.id
  )

  const listed = (await page.evaluate(() =>
    window.novalistRpc.request('entities/list', ['ship'])
  )) as { name: string }[]
  expect(listed.map((s) => s.name)).toContain('The Kestrel')

  const stored = JSON.stringify(
    await page.evaluate(
      (id: string) => window.novalistRpc.request('entities/get', ['ship', id]),
      ship.id
    )
  )
  expect(stored, 'the typed field did not persist').toContain('420')

  await app.close()
})
