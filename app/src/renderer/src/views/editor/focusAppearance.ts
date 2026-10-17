import { useSettingsStore } from '../../stores/settingsStore'
import { useShellStore } from '../../stores/shellStore'
import { focusColor, focusImage } from '../../stores/focusPreferences'
import type { EditorWindow } from './editorBridge'

function pageInk(color: string, token: (name: string) => string): string {
  const channels = [1, 3, 5].map((offset) => {
    const value = parseInt(color.slice(offset, offset + 2), 16) / 255
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
  })
  const luminance = channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722
  return token(luminance > 0.179 ? '--nl-focus-ink-dark' : '--nl-focus-ink-light')
}

export function applyFocusAppearance(editor: EditorWindow, token: (name: string) => string): void {
  const shell = useShellStore.getState()
  const focused = shell.focusMode && shell.mainView === 'write' && !shell.extView && !window.novalist.isMobile
  const view = useSettingsStore.getState().view
  const page = focusColor(view?.global.focusPageColor)
  const background = focusColor(view?.global.focusBackgroundColor) ??
    token(view?.effective.pageViewEnabled ? '--nl-focus-default-surround' : '--nl-surface-editor')
  const image = focusImage(view?.global.focusBackgroundImage)
  const root = editor.document.documentElement
  if (!editor.document.getElementById('focus-appearance')) {
    const link = editor.document.createElement('link')
    link.id = 'focus-appearance'
    link.rel = 'stylesheet'
    link.href = './focus-appearance.css'
    editor.document.head.append(link)
  }
  editor.document.body.classList.toggle('focus-appearance', focused)
  root.style.setProperty('--nl-focus-page', page ?? token(view?.effective.pageViewEnabled ? '--nl-surface-card' : '--nl-surface-editor'))
  root.style.setProperty('--nl-focus-ink', page ? pageInk(page, token) : token('--nl-text'))
  const pane = editor.frameElement?.closest<HTMLElement>('.editor-pane')
  pane?.style.setProperty('--nl-focus-background', background)
  pane?.style.setProperty('--nl-focus-image', image ? `url("${image}")` : 'none')
}
