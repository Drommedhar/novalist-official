/**
 * Builds the demo project used for App Store and manual screenshots.
 *
 * Drives the app's own JSON-RPC surface through a real Electron launch rather
 * than writing .novalist JSON by hand, so the result is always in exactly the
 * format the current backend reads back.
 *
 * Prerequisites: `npm --prefix app run build` and a debug build of
 * Novalist.Backend (the dev backend path the main process resolves).
 *
 * Usage: node tools/screenshots/make-demo-project.mjs <output-dir>
 */
import { _electron as electron } from 'playwright'
import { mkdtempSync, rmSync, existsSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, dirname, basename, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import {
  BOOK_NAME, CHAPTERS, CHARACTERS, LOCATIONS, ITEMS, LORE,
  PLOTLINES, PLOT_CELLS, TIMELINE_EVENTS, GOALS
} from './demo-content.mjs'

const HERE = dirname(fileURLToPath(import.meta.url))
const APP_DIR = resolve(HERE, '..', '..', 'app')

const outDir = process.argv[2]
if (!outDir) throw new Error('usage: make-demo-project.mjs <output-dir>')
if (existsSync(outDir)) rmSync(outDir, { recursive: true, force: true })

const settingsDir = mkdtempSync(join(tmpdir(), 'nl-demo-settings-'))
const env = Object.fromEntries(
  Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
)
env.NOVALIST_SETTINGS_DIR = settingsDir
env.NOVALIST_NO_SPLASH = '1'

const app = await electron.launch({ args: [join(APP_DIR, 'out/main/index.js')], cwd: APP_DIR, env })
const page = await app.firstWindow()
await page.locator('.status-backend.connected').waitFor({ timeout: 60_000 })

/** Calls a backend RPC method from the renderer and returns the raw result. */
const rpc = (method, params = []) =>
  page.evaluate(([m, p]) => window.novalistRpc.request(m, p), [method, params])

const strip = (html) =>
  html.replace(/<[^>]+>/g, ' ').replace(/&[a-z]+;/g, ' ').replace(/\s+/g, ' ').trim()

console.log('creating project...')
await rpc('project/create', [dirname(outDir), basename(outDir), BOOK_NAME])

// A fresh project ships with a placeholder chapter; drop it so the binder shows
// only the demo structure.
let state = await rpc('project/getState')
for (const ch of state.chapters) await rpc('project/deleteChapter', [ch.guid])

console.log('creating chapters and scenes...')
for (const chapter of CHAPTERS) {
  state = await rpc('project/createChapter', [chapter.title])
  const created = state.chapters[state.chapters.length - 1]
  await rpc('project/setChapterAct', [created.guid, chapter.act])
  await rpc('project/setChapterStatus', [created.guid, chapter.status])
  for (const scene of chapter.scenes) {
    state = await rpc('project/createScene', [created.guid, scene.title])
    const chNow = state.chapters.find((c) => c.guid === created.guid)
    const sceneId = chNow.scenes[chNow.scenes.length - 1].id
    await rpc('scenes/write', [created.guid, sceneId, scene.html, strip(scene.html)])
    await rpc('scenes/setSynopsis', [created.guid, sceneId, scene.synopsis])
    if (scene.notes) await rpc('scenes/setNotes', [created.guid, sceneId, scene.notes])
    if (scene.pov) await rpc('scenes/setPov', [created.guid, sceneId, scene.pov])
  }
}

console.log('creating codex entities...')
/** Creates one entity, sets its scalar fields, then its aliases/sections/relationships. */
async function makeEntity(type, spec) {
  const created = await rpc('entities/create', [type, spec.name])
  const id = created.id
  if (spec.fields) await rpc('entities/update', [type, id, spec.fields])
  await rpc('entities/updateLists', [
    type, id, spec.aliases ?? null, spec.sections ?? null, spec.relationships ?? null
  ])
  return id
}

for (const c of CHARACTERS) {
  await makeEntity('character', { ...c, fields: { ...c.fields, surname: c.surname } })
}
for (const l of LOCATIONS) await makeEntity('location', l)
for (const i of ITEMS) await makeEntity('item', i)
for (const l of LORE) await makeEntity('lore', l)

console.log('creating plotlines...')
const plotIds = {}
for (const name of PLOTLINES) {
  const grid = await rpc('plot/createPlotline', [name])
  plotIds[name] = grid.plotlines[grid.plotlines.length - 1].id
}

state = await rpc('project/getState')
for (const ch of state.chapters) {
  for (const sc of ch.scenes) {
    for (const line of PLOT_CELLS[sc.title] ?? []) {
      if (plotIds[line]) await rpc('plot/toggle', [ch.guid, sc.id, plotIds[line]])
    }
  }
}

console.log('creating timeline events...')
for (const e of TIMELINE_EVENTS) {
  await rpc('timeline/saveEvent', [null, e.title, e.date, e.description, e.category, null])
}

console.log('setting goals...')
await rpc('dashboard/setGoals', [GOALS.daily, GOALS.project, null])

const final = await rpc('project/getState')
const scenes = final.chapters.flatMap((c) => c.scenes)
console.log(
  `done: ${final.chapters.length} chapters, ${scenes.length} scenes, ` +
    `${scenes.reduce((n, s) => n + s.wordCount, 0)} words at ${outDir}`
)

await app.close()
rmSync(settingsDir, { recursive: true, force: true })
