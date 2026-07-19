import { useShellStore, type MainView } from '../stores/shellStore'

export interface HotkeyAction {
  actionId: string
  gesture: string
  labelKey: string
  run(): void
}

/**
 * Hotkey registry using the Avalonia KeyGesture string grammar
 * ("Ctrl+Shift+P") so descriptors stay compatible; Cmd maps to Ctrl on macOS.
 */
export function matchGesture(event: KeyboardEvent, gesture: string): boolean {
  const parts = gesture.split('+')
  const key = parts[parts.length - 1].toLowerCase()
  const needCtrl = parts.includes('Ctrl')
  const needShift = parts.includes('Shift')
  const needAlt = parts.includes('Alt')
  const ctrl = event.ctrlKey || event.metaKey
  const eventKey = event.key.length === 1 ? event.key.toLowerCase() : event.key.toLowerCase()
  const normalized = key.startsWith('d') && key.length === 2 ? key.slice(1) : key
  return (
    ctrl === needCtrl &&
    event.shiftKey === needShift &&
    event.altKey === needAlt &&
    eventKey === normalized
  )
}

const NAV_VIEWS: { gesture: string; view: MainView }[] = [
  { gesture: 'Ctrl+D1', view: 'write' },
  { gesture: 'Ctrl+D2', view: 'dashboard' },
  { gesture: 'Ctrl+D3', view: 'timeline' },
  { gesture: 'Ctrl+D4', view: 'codex' },
  { gesture: 'Ctrl+D5', view: 'manuscript' },
  { gesture: 'Ctrl+D6', view: 'calendar' },
  { gesture: 'Ctrl+D7', view: 'relationships' },
  { gesture: 'Ctrl+D8', view: 'plotGrid' },
  { gesture: 'Ctrl+D9', view: 'research' }
]

export function buildDefaultHotkeys(): HotkeyAction[] {
  const shell = (): ReturnType<typeof useShellStore.getState> => useShellStore.getState()
  const actions: HotkeyAction[] = NAV_VIEWS.map(({ gesture, view }) => ({
    actionId: `app.nav.${view}`,
    gesture,
    labelKey: `shell.view.${view}`,
    run: () => shell().setMainView(view)
  }))
  actions.push(
    {
      actionId: 'app.panels.binder',
      gesture: 'Ctrl+B',
      labelKey: 'shell.toggleBinder',
      run: () => shell().toggleBinder()
    },
    {
      actionId: 'app.panels.inspector',
      gesture: 'Ctrl+Shift+B',
      labelKey: 'shell.toggleInspector',
      run: () => shell().toggleInspector()
    },
    {
      actionId: 'app.edit.findReplace',
      gesture: 'Ctrl+Shift+F',
      labelKey: 'findReplace.title',
      run: () => shell().setFindReplaceOpen(true)
    },
    {
      actionId: 'app.view.focus',
      gesture: 'Alt+F',
      labelKey: 'menu.focusMode',
      run: () => shell().toggleFocusMode()
    },
    {
      actionId: 'app.commandPalette',
      gesture: 'Ctrl+Shift+P',
      labelKey: 'commandPalette.placeholder',
      run: () => shell().setCommandPaletteOpen(true)
    }
  )
  return actions
}

/**
 * The actions currently bound by {@link installHotkeys}. Kept so hotkeys
 * forwarded from inside the editor iframe (which never reach the window
 * keydown listener) can be routed through the same registry.
 */
let installedActions: HotkeyAction[] = []

/**
 * Dispatches a hotkey that originated inside the editor iframe. editor.html
 * posts {@code { key, code, ctrlKey, shiftKey, altKey }} for modified keys and
 * function keys; we rebuild a minimal event and reuse {@link matchGesture} so
 * global shortcuts fire even while the caret is in the editor. Returns true
 * when an action ran.
 */
export function dispatchForwardedHotkey(payload: {
  key: string
  code: string
  ctrlKey: boolean
  shiftKey: boolean
  altKey: boolean
}): boolean {
  const synthetic = {
    key: payload.key,
    code: payload.code,
    ctrlKey: payload.ctrlKey,
    metaKey: false,
    shiftKey: payload.shiftKey,
    altKey: payload.altKey
  } as KeyboardEvent
  for (const action of installedActions) {
    if (matchGesture(synthetic, action.gesture)) {
      action.run()
      return true
    }
  }
  return false
}

export function installHotkeys(actions: HotkeyAction[]): () => void {
  installedActions = actions
  const onKeyDown = (event: KeyboardEvent): void => {
    const target = event.target as HTMLElement | null
    const inField =
      target?.tagName === 'INPUT' ||
      target?.tagName === 'TEXTAREA' ||
      target?.isContentEditable === true
    for (const action of actions) {
      if (!matchGesture(event, action.gesture)) continue
      // Plain Ctrl+B etc. still fire in fields only when they carry modifiers
      // beyond what text editing uses; navigation gestures always take priority.
      if (inField && !event.ctrlKey && !event.metaKey) continue
      event.preventDefault()
      action.run()
      return
    }
  }
  window.addEventListener('keydown', onKeyDown)
  return () => {
    window.removeEventListener('keydown', onKeyDown)
    if (installedActions === actions) installedActions = []
  }
}
