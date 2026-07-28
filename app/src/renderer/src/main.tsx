import React from 'react'
import ReactDOM from 'react-dom/client'
import './i18n'
import './styles/tokens.css'
import './styles/base.css'
import { AppShell } from './shell/AppShell'
import { useProjectStore } from './stores/projectStore'
import { useShellStore } from './stores/shellStore'
import { useCodexStore } from './stores/codexStore'
import { useWikiStore } from './stores/wikiStore'
import { useSettingsStore } from './stores/settingsStore'
import { rpc } from './rpc/client'

// Store/RPC access for end-to-end tests (Playwright drives the real app through these).
window.novalistStores = {
  project: useProjectStore,
  shell: useShellStore,
  codex: useCodexStore,
  wiki: useWikiStore,
  settings: useSettingsStore
}
window.novalistRpc = rpc

const root = document.documentElement
root.dataset.material = window.novalist.material
// The light theme was removed; Default is dark. Named themes pin data-theme via
// settings (applyThemeTokens). Set a dark baseline so there is no light flash
// before settings load.
root.dataset.theme = 'dark'

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <AppShell />
  </React.StrictMode>
)
