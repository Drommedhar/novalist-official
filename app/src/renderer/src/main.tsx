import React from 'react'
import ReactDOM from 'react-dom/client'
import './i18n'
import './styles/tokens.css'
import './styles/base.css'
import { AppShell } from './shell/AppShell'
import { DetachedPane, type DetachedRequest } from './shell/DetachedPane'
import type { MainView } from './stores/shellStore'
import { useProjectStore } from './stores/projectStore'
import { useShellStore } from './stores/shellStore'
import { useCodexStore } from './stores/codexStore'
import { useWikiStore } from './stores/wikiStore'
import { useSettingsStore } from './stores/settingsStore'
import { useExtensionsStore } from './stores/extensionsStore'
import { rpc } from './rpc/client'
import { postThemeToFrame, themeTokens, watchTheme } from './shell/extensionTheme'

// Store/RPC access for end-to-end tests (Playwright drives the real app through these).
window.novalistStores = {
  project: useProjectStore,
  shell: useShellStore,
  codex: useCodexStore,
  wiki: useWikiStore,
  settings: useSettingsStore,
  extensions: useExtensionsStore
}
window.novalistRpc = rpc
// The theme bridge for extension frames. Exposed for the same reason as the
// stores - an end-to-end test drives the real app - and because a renderer
// plugin that builds a frame of its own needs to theme it the same way the
// contributed webviews are themed.
window.novalistExtensionTheme = { themeTokens, postThemeToFrame, watchTheme }
// Same reason the stores are here: an end-to-end test drives the real app, and
// what a plugin contributed is otherwise only visible as pixels.
void import('./shell/pluginHost').then((host) => {
  window.novalistPlugins = {
    commands: host.pluginCommands,
    statusItems: host.pluginStatusItems,
    reload: host.reloadRendererPlugins
  }
})

const root = document.documentElement
root.dataset.material = window.novalist.material
// The light theme was removed; Default is dark. Named themes pin data-theme via
// settings (applyThemeTokens). Set a dark baseline so there is no light flash
// before settings load.
root.dataset.theme = 'dark'

// A window opened to hold one torn-off pane says so in its URL - along with the
// project and scene it was torn off, so it opens on what the writer was looking
// at. It runs the same renderer, so the view inside is the real one rather than
// a picture.
const params = new URLSearchParams(window.location.search)
const detachedView = params.get('pane')
const detached: DetachedRequest | null = detachedView
  ? {
      view: detachedView as MainView,
      projectPath: params.get('project'),
      chapterGuid: params.get('chapter'),
      sceneId: params.get('scene')
    }
  : null

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    {detached ? <DetachedPane request={detached} /> : <AppShell />}
  </React.StrictMode>
)
