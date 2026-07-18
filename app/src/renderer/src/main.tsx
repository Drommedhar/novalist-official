import React from 'react'
import ReactDOM from 'react-dom/client'
import './i18n'
import './styles/tokens.css'
import './styles/base.css'
import { AppShell } from './shell/AppShell'

const root = document.documentElement
root.dataset.material = window.novalist.material

const media = window.matchMedia('(prefers-color-scheme: light)')
function applyTheme(): void {
  root.dataset.theme = media.matches ? 'light' : 'dark'
}
applyTheme()
media.addEventListener('change', applyTheme)

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <AppShell />
  </React.StrictMode>
)
