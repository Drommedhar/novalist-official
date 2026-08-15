import { test, expect, _electron as electron } from '@playwright/test'
import { existsSync, mkdirSync, mkdtempSync, writeFileSync, copyFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { copyProject } from './copyProject'
import { evaluateWhenReady } from './appReady'
import { REAL_PROJECT } from './realProject'

/**
 * An extension running JavaScript inside the interface.
 *
 * A webview is sandboxed and cannot touch anything the writer is looking at.
 * This can, which is the point of it and the cost of it.
 *
 * What is asserted here is the half no unit test reaches: that the script runs
 * in the real renderer and that what it adds appears on screen. A plugin host
 * that loads nothing fails silently - every other test stays green and the
 * writer's extension simply does nothing.
 */

test('an extension script runs in the interface and what it adds shows up', async () => {
  test.skip(!existsSync(join(REAL_PROJECT, '.novalist')), 'real project not available')
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-plugin-'))
  const projectCopy = join(workDir, 'project')
  copyProject(REAL_PROJECT, projectCopy)

  // Extensions are discovered under the settings folder, so the settings
  // folder this run uses is where the extension goes.
  const settingsDir = join(workDir, 'settings')
  const extensionDir = join(settingsDir, 'Extensions', 'Marker')
  mkdirSync(extensionDir, { recursive: true })

  // Any built copy of the sample will do: the extension only needs a loadable
  // assembly beside its manifest, and nothing in this test calls into it.
  const candidates = [
    join(process.cwd(), '..', 'Novalist.Sdk.Example', 'bin', 'Debug', 'net8.0', 'Novalist.Sdk.Example.dll'),
    join(process.cwd(), 'out', 'backend', 'Novalist.Sdk.Example.dll')
  ]
  const assembly = candidates.find((c) => existsSync(c)) ?? candidates[0]
  test.skip(!existsSync(assembly), 'sample extension assembly not built')
  copyFileSync(assembly, join(extensionDir, 'Novalist.Sdk.Example.dll'))

  writeFileSync(
    join(extensionDir, 'extension.json'),
    JSON.stringify({
      id: 'com.novalist.e2e.marker',
      name: 'Marker',
      version: '1.0.0',
      entryAssembly: 'Novalist.Sdk.Example.dll',
      contributes: { renderer: [{ entry: 'plugin.js', apiVersion: 1 }] }
    })
  )
  writeFileSync(
    join(extensionDir, 'plugin.js'),
    // Everything a plugin can do that leaves a mark on screen: put something in
    // the status bar, add a command, and call the host and prove the answer
    // came back.
    `export function activate(novalist) {
       novalist.setStatusItem('mark', 'PLUGIN-RAN');
       novalist.registerCommand('shout', 'PLUGIN-COMMAND', function () {});
       novalist.request('project/getState').then(function () {
         novalist.setStatusItem('rpc', 'PLUGIN-RPC-OK');
       });
     }`
  )

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_SETTINGS_DIR = settingsDir
  env.NOVALIST_NO_SPLASH = '1'

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })
  await evaluateWhenReady(page, async (root) => {
    const state = await window.novalistRpc.request('project/open', [root])
    window.novalistStores.project.getState().applyState(state as never)
  }, projectCopy)

  // Extensions load when the writer opens the Extensions view; the plugin host
  // reloads with them, which is what makes an installed extension work without
  // a restart.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('extensions'))
  await page.waitForTimeout(3000)

  // The backend half first, so a failure below says which of the two broke.
  const seen = await page.evaluate(async () =>
    JSON.stringify(await window.novalistRpc.request('extensions/rendererPlugins'))
  )
  expect(seen).toContain('plugin.js')
  expect(seen).not.toContain('"refused":"')

  // What the renderer actually did with it.
  const state = await page.evaluate(() => ({
    items: window.novalistPlugins?.statusItems().map((s) => s.text) ?? [],
    commands: window.novalistPlugins?.commands().map((c) => c.title) ?? []
  }))
  expect(state.items).toContain('PLUGIN-RAN')
  expect(state.commands).toContain('PLUGIN-COMMAND')

  // Back to a view that has a status bar: Extensions is a system view and
  // deliberately carries none of the project chrome, so the item an
  // extension contributes to the status bar cannot be seen from inside it.
  await page.evaluate(() => window.novalistStores.shell.getState().setMainView('dashboard'))

  // On screen, not merely returned by an RPC.
  await expect(page.locator('.status-plugin-item', { hasText: 'PLUGIN-RAN' })).toBeVisible({
    timeout: 30_000
  })
  // And the host answered a call the plugin made.
  await expect(page.locator('.status-plugin-item', { hasText: 'PLUGIN-RPC-OK' })).toBeVisible({
    timeout: 30_000
  })

  await app.close()
})
