import { useShellStore, type MainView } from '../stores/shellStore'

export interface HotkeyAction {
  actionId: string
  /** Currently active gesture (persisted override, else {@link defaultGesture}). */
  gesture: string
  /** Factory default gesture, used for reset and the "modified" indicator. */
  defaultGesture: string
  /** Localization key for the settings category the action groups under. */
  categoryKey: string
  labelKey: string
  run(): void
}

/**
 * Persisted per-action gesture overrides (action ID → gesture string), mirrored
 * from {@code AppSettings.HotkeyBindings}. {@link buildDefaultHotkeys} folds
 * these over the defaults, and {@link applyCustomGestures} keeps the already
 * installed actions in sync when they change while the app is running.
 */
let customGestures: Record<string, string> = {}

/**
 * Applies persisted gesture overrides. Updates the module map and mutates the
 * gestures of the currently installed actions in place, so the live keydown
 * listener (which closes over those same objects) and the command palette pick
 * up the change without a reinstall.
 */
export function applyCustomGestures(map: Record<string, string>): void {
  customGestures = map ?? {}
  for (const action of installedActions) {
    action.gesture = customGestures[action.actionId] ?? action.defaultGesture
  }
}

/**
 * Canonical, comparable form of a gesture ("ctrl+shift+1") used for conflict
 * detection in the settings UI. Mirrors {@link matchGesture}'s normalization so
 * "Ctrl+D1" and "Ctrl+1" collapse to the same key.
 */
export function canonicalGesture(gesture: string): string {
  const parts = gesture.split('+')
  const key = parts[parts.length - 1].toLowerCase()
  const normalized = key.startsWith('d') && key.length === 2 ? key.slice(1) : key
  const ctrl = parts.includes('Ctrl') ? 'ctrl+' : ''
  const shift = parts.includes('Shift') ? 'shift+' : ''
  const alt = parts.includes('Alt') ? 'alt+' : ''
  return `${ctrl}${shift}${alt}${normalized}`
}

/**
 * Builds an Avalonia-style gesture string from a keydown event, or null for a
 * modifier-only press (which cannot be a binding on its own). Digits become
 * "D1".."D9" and single letters upper-case, matching the default descriptors.
 */
export function eventToGesture(event: {
  key: string
  ctrlKey: boolean
  metaKey: boolean
  shiftKey: boolean
  altKey: boolean
}): string | null {
  const { key } = event
  if (key === 'Control' || key === 'Shift' || key === 'Alt' || key === 'Meta') return null
  const parts: string[] = []
  if (event.ctrlKey || event.metaKey) parts.push('Ctrl')
  if (event.shiftKey) parts.push('Shift')
  if (event.altKey) parts.push('Alt')
  if (/^[0-9]$/.test(key)) parts.push(`D${key}`)
  else if (key.length === 1) parts.push(key.toUpperCase())
  else parts.push(key)
  return parts.join('+')
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

/** Descriptor before the active gesture is resolved from persisted overrides. */
type HotkeyDef = Omit<HotkeyAction, 'gesture'>

export function buildDefaultHotkeys(): HotkeyAction[] {
  const shell = (): ReturnType<typeof useShellStore.getState> => useShellStore.getState()
  const defs: HotkeyDef[] = NAV_VIEWS.map(({ gesture, view }) => ({
    actionId: `app.nav.${view}`,
    defaultGesture: gesture,
    categoryKey: 'hotkeys.category.navigation',
    labelKey: `shell.view.${view}`,
    run: () => shell().setMainView(view)
  }))
  defs.push(
    {
      actionId: 'app.panels.binder',
      defaultGesture: 'Ctrl+B',
      categoryKey: 'hotkeys.category.panels',
      labelKey: 'shell.toggleBinder',
      run: () => shell().toggleBinder()
    },
    {
      actionId: 'app.panels.inspector',
      defaultGesture: 'Ctrl+Shift+B',
      categoryKey: 'hotkeys.category.panels',
      labelKey: 'shell.toggleInspector',
      run: () => shell().toggleInspector()
    },
    {
      actionId: 'app.panels.sceneNotes',
      defaultGesture: 'Ctrl+Shift+N',
      categoryKey: 'hotkeys.category.panels',
      labelKey: 'shell.toggleSceneNotes',
      run: () => shell().toggleNotesDock()
    },
    {
      actionId: 'app.edit.findReplace',
      defaultGesture: 'Ctrl+Shift+F',
      categoryKey: 'hotkeys.category.editor',
      labelKey: 'findReplace.title',
      run: () => shell().setFindReplaceOpen(true)
    },
    {
      actionId: 'app.view.focus',
      defaultGesture: 'Alt+F',
      categoryKey: 'hotkeys.category.panels',
      labelKey: 'menu.focusMode',
      run: () => shell().toggleFocusMode()
    },
    {
      actionId: 'app.commandPalette',
      defaultGesture: 'Ctrl+Shift+P',
      categoryKey: 'hotkeys.category.general',
      labelKey: 'commandPalette.placeholder',
      run: () => shell().setCommandPaletteOpen(true)
    }
  )
  return defs.map((def) => ({
    ...def,
    gesture: customGestures[def.actionId] ?? def.defaultGesture
  }))
}

/**
 * The actions currently bound by {@link installHotkeys}. Kept so hotkeys
 * forwarded from inside the editor iframe (which never reach the window
 * keydown listener) can be routed through the same registry.
 */
let installedActions: HotkeyAction[] = []

/**
 * Extension-contributed hotkey actions (IHotkeyContributor), fetched after the
 * built-in set is installed. The live keydown listener and the editor-forwarded
 * dispatcher both consult this module-level array on every event, so a later
 * {@link setExtensionHotkeys} takes effect without reinstalling the listener.
 */
let extensionActions: HotkeyAction[] = []

/** Replaces the set of extension-contributed hotkeys. */
export function setExtensionHotkeys(actions: HotkeyAction[]): void {
  extensionActions = actions
}

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
  for (const action of [...installedActions, ...extensionActions]) {
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
    for (const action of [...actions, ...extensionActions]) {
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
