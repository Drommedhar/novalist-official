/**
 * Second pass over the generated demo project. Fills in everything that would
 * otherwise leave a view looking empty in a screenshot:
 *
 *   - imports the generated cover and banner art (Dashboard, welcome screen)
 *   - lays down a plausible writing history (progress chart, streak, daily bar)
 *   - gives every scene a story date and anchors the Calendar there
 *
 * Usage: node tools/screenshots/enrich-demo.mjs <project-dir> <art-dir>
 */
import { _electron as electron } from 'playwright'
import { writeFileSync, readFileSync, mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { SCENE_DATES, CALENDAR_ANCHOR, GOALS } from './demo-content.mjs'

const HERE = dirname(fileURLToPath(import.meta.url))
const APP_DIR = resolve(HERE, '..', '..', 'app')

const [projectDir, artDir] = process.argv.slice(2)
if (!projectDir || !artDir) throw new Error('usage: enrich-demo.mjs <project-dir> <art-dir>')

const settingsDir = mkdtempSync(join(tmpdir(), 'nl-enrich-'))
const env = Object.fromEntries(
  Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
)
env.NOVALIST_SETTINGS_DIR = settingsDir
env.NOVALIST_NO_SPLASH = '1'

const app = await electron.launch({ args: [join(APP_DIR, 'out/main/index.js')], cwd: APP_DIR, env })
const page = await app.firstWindow()
await page.locator('.status-backend.connected').waitFor({ timeout: 60_000 })

const rpc = (method, params = []) =>
  page.evaluate(([m, p]) => window.novalistRpc.request(m, p), [method, params])

await rpc('project/open', [projectDir])

console.log('importing cover and banner...')
await rpc('dashboard/setCover', [join(artDir, 'cover.png')])
await rpc('dashboard/setBanner', [join(artDir, 'banner.png')])

// The Calendar plots scenes by resolved story date and opens on the stored
// anchor. Without both, it renders an empty grid 178 years after the book.
console.log('dating scenes...')
const state = await rpc('project/getState')
let dated = 0
for (const ch of state.chapters) {
  for (const sc of ch.scenes) {
    const date = SCENE_DATES[sc.title]
    if (!date) continue
    await rpc('project/setSceneDateRange', [ch.guid, sc.id, date, date, ''])
    dated++
  }
}
await rpc('calendar/setAnchor', [CALENDAR_ANCHOR])
console.log(`  dated ${dated} scenes, calendar anchored at ${CALENDAR_ANCHOR}`)

const bookId = state.activeBookId
const sceneIds = state.chapters.flatMap((c) => c.scenes.map((s) => s.id))
await app.close()
rmSync(settingsDir, { recursive: true, force: true })

// --- Writing history ---------------------------------------------------------
// Deterministic so a re-shoot produces the same chart. The most recent fortnight
// always clears the daily goal, which is what the streak counter reads back.
let seed = 20260726
const rand = () => {
  seed = (seed * 1103515245 + 12345) & 0x7fffffff
  return seed / 0x7fffffff
}
const iso = (d) => d.toISOString().slice(0, 10)
const today = new Date()
today.setHours(12, 0, 0, 0)

const lines = []
for (let back = 44; back >= 0; back--) {
  const day = new Date(today)
  day.setDate(day.getDate() - back)
  const resting = back > 13 && rand() < 0.22
  if (resting) continue
  const target =
    back <= 13 ? GOALS.daily + 50 + Math.floor(rand() * 900) : 400 + Math.floor(rand() * 1800)
  let left = target
  const touched = 1 + Math.floor(rand() * 3)
  for (let i = 0; i < touched && left > 0; i++) {
    const delta = i === touched - 1 ? left : Math.max(1, Math.floor(left * (0.35 + rand() * 0.4)))
    left -= delta
    lines.push(
      JSON.stringify({
        date: iso(day),
        sceneId: sceneIds[Math.floor(rand() * sceneIds.length)],
        bookId,
        words: 200 + Math.floor(rand() * 300),
        delta
      })
    )
  }
}
writeFileSync(join(projectDir, '.novalist', 'word-history.jsonl'), lines.join('\n') + '\n')
console.log(`wrote ${lines.length} word-history entries`)

// The daily bar reads (total words - baseline). The app rebaselines to the full
// total on the first open of each day, which would render 0%; pre-seed today's
// baseline lower so the bar shows a partially-finished session.
const settingsPath = join(projectDir, '.novalist', 'settings.json')
const settings = JSON.parse(readFileSync(settingsPath, 'utf8'))
const totalWords = settings.wordCountGoals.dailyBaselineWords ?? 0
settings.wordCountGoals.dailyBaselineDate = iso(today)
settings.wordCountGoals.dailyBaselineWords = Math.max(0, totalWords - 840)
writeFileSync(settingsPath, JSON.stringify(settings, null, 2))
console.log(`daily baseline set for ${iso(today)}`)
