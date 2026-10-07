// aislop-ignore-file ai-slop/console-leftover -- Screenshot CLI reports capture progress and output paths to its invoking terminal.
/**
 * Captures raw macOS screenshots of every view we ship in the manual and on the
 * App Store.
 *
 * Runs the real Electron app against the generated demo project. Output PNGs
 * still carry the Liquid Glass alpha channel; compositing onto an opaque
 * backdrop is a separate step (composite.sh) so a re-shoot never has to repeat
 * the slow part.
 *
 * Usage: node tools/screenshots/capture-desktop.mjs <project-dir> <out-dir> [scale]
 */
import { createRequire } from 'node:module'

const { _electron: electron } = createRequire(new URL('../../app/package.json', import.meta.url))('@playwright/test')
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const HERE = dirname(fileURLToPath(import.meta.url))
const APP_DIR = resolve(HERE, '..', '..', 'app')

const [projectDir, outDir, scaleArg] = process.argv.slice(2)
if (!projectDir || !outDir) throw new Error('usage: capture-desktop.mjs <project-dir> <out-dir> [scale]')
// 2 gives 2880x1800 from a 1440x900 window, which is an accepted App Store size.
const SCALE = Number(scaleArg ?? 2)
const WIDTH = 1440
const HEIGHT = 900

mkdirSync(outDir, { recursive: true })
// One settings dir across both launches: opening the project in pass 1 is what
// puts it in the recents list that pass 2 photographs on the welcome screen.
const settingsDir = mkdtempSync(join(tmpdir(), 'nl-shot-settings-'))
const env = Object.fromEntries(
  Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
)
env.NOVALIST_SETTINGS_DIR = settingsDir
env.NOVALIST_NO_SPLASH = '1'
const appStoreEdition = env.NOVALIST_FORCE_MAS === '1'
const captures = new Map()

async function launch() {
  const app = await electron.launch({
    args: [join(APP_DIR, 'out/main/index.js'), `--force-device-scale-factor=${SCALE}`, `--user-data-dir=${join(settingsDir, 'electron')}`],
    cwd: APP_DIR,
    env
  })
  const page = await app.firstWindow()
  page.setDefaultTimeout(30_000)
  await page.locator('.status-backend.connected').waitFor({ timeout: 60_000 })
  await page.evaluate(() => {
    window.novalistStores.onboarding.getState().skipTour()
    window.novalistStores.onboarding.getState().setTipsEnabled(false)
    window.novalistStores.shell.getState().setTourOpen(false)
  })
  await app.evaluate(
    ({ BrowserWindow }, [w, h]) => {
      BrowserWindow.getAllWindows()[0].setBounds({ x: 40, y: 40, width: w, height: h })
    },
    [WIDTH, HEIGHT]
  )
  await page.waitForTimeout(600)
  return { app, page }
}

let { app, page } = await launch()

const shot = async (name) => {
  await page.waitForTimeout(900)
  await page.evaluate(() => document.fonts.ready)
  if (await page.locator('.tour-card:visible').count()) throw new Error('Tour obscures the capture')
  await page.screenshot({ path: join(outDir, `${name}.png`), omitBackground: true })
  captures.set(name, { name, view: await page.evaluate(() => window.novalistStores.shell.getState().mainView) })
  console.log(`  captured ${name}`)
}
const setView = (view) =>
  page.evaluate((v) => window.novalistStores.shell.getState().setMainView(v), view)
const setShell = (patch) => page.evaluate((p) => window.novalistStores.shell.setState(p), patch)

await page.evaluate(async (root) => {
  const state = await window.novalistRpc.request('project/open', [root])
  window.novalistStores.project.getState().applyState(state)
}, projectDir)
await page.waitForTimeout(2500)

console.log('capturing write views...')
await setView('write')
await setShell({ binderVisible: true, inspectorVisible: true })
await page.locator('.binder-scene-row').nth(2).click()
await page.frameLocator('.editor-frame').locator('#editor').waitFor({ timeout: 30_000 })
await page.locator('.binder').evaluate((element) => {
  for (const child of [element, ...element.querySelectorAll('*')]) {
    if (child.scrollHeight > child.clientHeight) child.scrollTop = 0
  }
})
await shot('interface-overview')

await setShell({ inspectorVisible: true, notesDockVisible: true })
await shot('editor')
await setShell({ notesDockVisible: false, inspectorVisible: true })

const VIEWS = [
  ['dashboard', 'dashboard'],
  ['manuscript', 'manuscript'],
  ['timeline', 'timeline'],
  ['plotGrid', 'plot-grid'],
  ['relationships', 'relationships'],
  ['wiki', 'wiki'],
  ['research', 'research'],
  ['export', 'export'],
  ['gallery', 'gallery'],
  ['maps', 'maps']
]
for (const [view, name] of VIEWS) {
  console.log(`capturing ${name}...`)
  await setView(view)
  await page.waitForTimeout(1400)
  if (view === 'dashboard') await page.locator('.dashboard-metrics').evaluate((element) => element.scrollIntoView({ block: 'start' }))
  if (view === 'relationships') await page.locator('.relationships-node-group').filter({ hasText: 'Mira Aldencourt' }).click()
  if (view === 'wiki') await page.locator('.wiki-entry-title').getByText('Mira Aldencourt', { exact: true }).click()
  if (view === 'research') await page.getByRole('button', { name: /Reading the hidden chart/ }).click()
  await shot(name)
}

// The Calendar needs month mode: the demo's story dates span a month, and the
// default week view would show one or two scenes at most.
console.log('capturing calendar...')
await setView('calendar')
await page.waitForTimeout(2000)
await page.getByRole('button', { name: 'Month' }).click()
await page.waitForTimeout(1600)
await shot('calendar')

// Codex needs a selected entry, otherwise the detail pane is an empty prompt.
console.log('capturing codex...')
await setView('codex')
await page.waitForTimeout(1400)
await page.getByText('Mira Aldencourt', { exact: true }).first().click()
await page.waitForTimeout(1200)
await shot('codex')

// Manuscript's corkboard is a distinct enough surface to be worth its own shot.
console.log('capturing corkboard...')
await setView('manuscript')
await page.waitForTimeout(900)
await page.getByRole('button', { name: 'Corkboard', exact: true }).click()
await page.waitForTimeout(1200)
await shot('corkboard')
if (!appStoreEdition) await shot('manuscript')

console.log('capturing overlays...')
await setView('write')
await page.waitForTimeout(800)
await setShell({ commandPaletteOpen: true })
await shot('command-palette')
await setShell({ commandPaletteOpen: false })
await page.locator('.palette-card').waitFor({ state: 'hidden' })

await setShell({ quickOpenOpen: true })
await page.getByRole('combobox', { name: 'Search everything', exact: true }).fill('chart')
await page.getByRole('dialog', { name: 'Search everything', exact: true }).locator('.palette-item').first().waitFor()
await shot('quick-open')
await setShell({ quickOpenOpen: false })

await setShell({ focusMode: true, binderVisible: false, inspectorVisible: false })
await shot('focus-mode')

await app.close()

console.log('capturing start-screen...')
;({ app, page } = await launch())
await page.waitForTimeout(1500)
await shot('start-screen')
await app.close()

rmSync(settingsDir, { recursive: true, force: true })
writeFileSync(join(outDir, 'capture-manifest.json'), JSON.stringify({ edition: appStoreEdition ? 'Mac App Store' : 'direct-download', width: WIDTH, height: HEIGHT, scale: SCALE, captures: [...captures.values()] }, null, 2) + '\n')
console.log('done')
