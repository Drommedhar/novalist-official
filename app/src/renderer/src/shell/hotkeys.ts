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
      actionId: 'app.commandPalette',
      gesture: 'Ctrl+Shift+P',
      labelKey: 'commandPalette.placeholder',
      run: () => shell().setCommandPaletteOpen(true)
    }
  )
  return actions
}

export function installHotkeys(actions: HotkeyAction[]): () => void {
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
  return () => window.removeEventListener('keydown', onKeyDown)
}
