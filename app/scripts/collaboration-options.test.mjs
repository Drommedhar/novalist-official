import assert from 'node:assert/strict'
import test from 'node:test'
import { build } from 'esbuild'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'

const bundle = await build({
  stdin: { resolveDir: fileURLToPath(new URL('..', import.meta.url)), loader: 'tsx', contents: `
    import React from 'react'
    import { createRoot } from 'react-dom/client'
    import { CollaborationCard } from './src/renderer/src/views/settings/CollaborationCard'
    const root = createRoot(document.getElementById('root'))
    export const mount = () => root.render(<CollaborationCard />)
    export const close = () => root.unmount()
  ` },
  bundle: true, write: false, platform: 'browser', format: 'iife', globalName: 'fixture', jsx: 'automatic',
  define: { 'process.env.NODE_ENV': '"production"' },
  plugins: [{ name: 'collaboration-boundaries', setup(build) {
    build.onResolve({ filter: /^(react-i18next|.*\/rpc\/client|.*\/hostedSessionStore|.*\/projectStore|.*\/CollaborationTransferStatus|.*\/SharedProjectCard)$/ }, args => ({
      path: args.path.endsWith('/hostedSessionStore') ? '/hostedSessionStore' : args.path, namespace: 'fixture'
    }))
    build.onLoad({ filter: /.*/, namespace: 'fixture' }, ({ path }) => ({ loader: 'tsx', contents:
      path === 'react-i18next' ? 'export const useTranslation = () => ({ t: key => key })' :
      path.endsWith('/rpc/client') ? 'export const rpc = window.rpc' :
      path.endsWith('/hostedSessionStore') ? `import { create } from 'zustand'; export const useHostedSessionStore = create(() => window.initialSession); window.sessions = useHostedSessionStore` :
      path.endsWith('/projectStore') ? 'export const useProjectStore = selector => selector({ isLoaded: window.projectLoaded })' :
      path.endsWith('/CollaborationTransferStatus') ? 'export const CollaborationTransferStatus = () => null' :
      'export const SharedProjectCard = () => <div data-shared />', resolveDir: fileURLToPath(new URL('..', import.meta.url)) }))
  } }]
})

async function fixture(t, { mobile = false, loaded = true, session = null, saved = null } = {}) {
  const dom = new JSDOM('<div id="root"></div>', { url: 'https://novalist.test/', runScripts: 'outside-only', pretendToBeVisual: true })
  const calls = [], links = []
  let resolveSteam
  const steam = new Promise(resolve => { resolveSteam = resolve })
  dom.window.initialSession = { session, saved, busy: null }
  dom.window.projectLoaded = loaded
  dom.window.novalist = { isMobile: mobile, pickFolder: async () => '/chosen-folder', openExternal: async url => { links.push(url); return true } }
  dom.window.rpc = { request: async method => {
    calls.push(method)
    if (method === 'collaboration/steam/status') return steam
    if (method === 'collaboration/account/configuration') return { authenticationRequired: false }
    throw new Error('Unexpected RPC: ' + method)
  } }
  dom.window.eval(bundle.outputFiles[0].text)
  t.after(() => { dom.window.fixture.close(); dom.window.close() })
  dom.window.fixture.mount()
  const waitFor = async condition => {
    const deadline = Date.now() + 2_000
    while (!condition()) {
      assert.ok(Date.now() < deadline, 'expected rendered collaboration state')
      await new Promise(resolve => setTimeout(resolve, 5))
    }
  }
  await waitFor(() => dom.window.document.querySelector('.collaboration-settings'))
  const mode = () => dom.window.document.querySelector('#set-collaboration-mode')
  const select = value => { mode().value = value; mode().dispatchEvent(new dom.window.Event('change', { bubbles: true })) }
  return { dom, calls, links, waitFor, mode, select, resolveSteam }
}

for (const mobile of [false, true]) test(`${mobile ? 'mobile' : 'desktop'} starts with TCP and discovers relay setup only when selected`, async t => {
  const f = await fixture(t, { mobile })
  f.resolveSteam({ available: false })
  assert.equal(f.mode().value, 'tcp')
  assert.equal(f.dom.window.document.querySelector('#set-collaboration-server'), null)
  if (mobile) assert.deepEqual(f.calls, [])
  f.select('hosted')
  await f.waitFor(() => f.dom.window.document.querySelector('#set-collaboration-server'))
  const buttons = [...f.dom.window.document.querySelectorAll('button')]
  buttons.find(button => button.textContent === 'collaboration.serverProject').click()
  buttons.find(button => button.textContent === 'collaboration.serverSetupGuide').click()
  assert.deepEqual(f.links, ['https://github.com/QF0xB/novalist-sync-server', 'https://github.com/QF0xB/novalist-sync-server/blob/main/docs/relay.md'])
  f.select('tcp')
  await f.waitFor(() => !f.dom.window.document.querySelector('#set-collaboration-server'))
  assert.equal(f.calls.some(method => method.startsWith('collaboration/account/')), false)
})

test('Steam availability selects Steam but a late result preserves the user’s connection choice', async t => {
  const f = await fixture(t)
  f.resolveSteam({ available: true })
  await f.waitFor(() => f.mode().value === 'steam')
  assert.ok([...f.dom.window.document.querySelectorAll('button')].some(button => button.textContent === 'collaboration.steamHost'))
  const chosen = await fixture(t)
  chosen.select('hosted')
  chosen.resolveSteam({ available: true })
  await chosen.waitFor(() => chosen.dom.window.document.querySelector('option[value="steam"]'))
  assert.equal(chosen.mode().value, 'hosted')
})

test('choosing a destination before Steam detection finishes keeps the selected folder', async t => {
  const f = await fixture(t, { loaded: false })
  const destination = () => f.dom.window.document.querySelector('.collaboration-destination-path').textContent
  const buttons = [...f.dom.window.document.querySelectorAll('button')]
  buttons.find(button => button.textContent === 'collaboration.parentFolder').click()
  await f.waitFor(() => destination().startsWith('/chosen-folder/'))
  f.resolveSteam({ available: true })
  await f.waitFor(() => f.dom.window.document.querySelector('option[value="steam"]'))
  assert.equal(f.mode().value, 'tcp')
  assert.equal(destination(), '/chosen-folder/collaboration.defaultFolderName')
})

test('a retained relay connection remains discoverable instead of reverting to the new default', async t => {
  const f = await fixture(t)
  f.dom.window.sessions.setState({ saved: { server: 'https://relay.example', projectId: 'saved-project' } })
  await f.waitFor(() => f.mode().value === 'hosted')
  f.resolveSteam({ available: true })
  await f.waitFor(() => f.dom.window.document.querySelector('option[value="steam"]'))
  assert.equal(f.mode().value, 'hosted')
  assert.ok(f.dom.window.document.querySelector('[data-shared]'))
})

test('ending an active relay session keeps its connection type despite Steam availability', async t => {
  const f = await fixture(t, { session: { sessionId: 'active', mode: 'hosted', server: 'https://relay.example' } })
  f.resolveSteam({ available: true })
  await f.waitFor(() => f.calls.includes('collaboration/steam/status'))
  assert.equal(f.mode(), null)
  f.dom.window.sessions.setState({ session: null })
  await f.waitFor(() => f.dom.window.document.querySelector('option[value="steam"]'))
  assert.equal(f.mode().value, 'hosted')
})
