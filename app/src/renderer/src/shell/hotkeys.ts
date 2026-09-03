import { COMMANDS } from './commands'

/**
 * Gestures: matching them, rebinding them, and dispatching what they run.
 *
 * This file used to *be* the list of things Novalist could do - an action
 * existed because somebody had given it a keyboard shortcut - and the command
 * palette, built from the same list, could therefore only ever offer the two
 * dozen commands that had one. The list lives in `commands.ts` now, and a
 * gesture is one optional property of a command. What is left here is the
 * keyboard: the grammar, the matching, the writer's own overrides, and the
 * listener that turns a keypress into a command.
 */

export { printCurrentView } from './printView'

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
 * The keys whose name in a gesture is not the character the keyboard reports.
 *
 * Zoom is the awkward one: "Ctrl+Plus" is pressed as Ctrl and the `=`/`+` key,
 * with or without Shift depending on the layout, so a gesture written the way a
 * menu writes it has to match all of them.
 */
const KEY_ALIASES: Record<string, string[]> = {
  plus: ['+', '='],
  minus: ['-', '_']
}

/**
 * Canonical, comparable form of a gesture ("ctrl+shift+1") used for conflict
 * detection in the settings UI. Mirrors {@link matchGesture}'s normalization so
 * "Ctrl+D1" and "Ctrl+1" collapse to the same key.
 */
export function canonicalGesture(gesture: string): string {
  const parts = gesture.split('+')
  const key = parts[parts.length - 1].toLowerCase()
  const digit = key.startsWith('d') && key.length === 2 ? key.slice(1) : key
  // "Ctrl+Plus" and a gesture the writer recorded by pressing Ctrl and the same
  // key are one binding, so they have to collapse to one name here or the
  // conflict warning in Settings never fires.
  const alias = Object.entries(KEY_ALIASES).find(([, keys]) => keys.includes(digit))
  const normalized = alias ? alias[0] : digit
  const ctrl = parts.includes('Ctrl') ? 'ctrl+' : ''
  // Shift is dropped for an aliased key, because matchGesture ignores it there:
  // whether `+` needed Shift is the layout's business, not the binding's.
  const shift = !alias && parts.includes('Shift') ? 'shift+' : ''
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
 * Whether this keypress is the gesture. Uses the Avalonia KeyGesture string
 * grammar ("Ctrl+Shift+P") so descriptors stay compatible; Cmd maps to Ctrl on
 * macOS.
 */
export function matchGesture(event: KeyboardEvent, gesture: string): boolean {
  // Most commands carry no gesture at all now that a hotkey is a property of a
  // command rather than the reason it exists. An unbound one must never match.
  if (gesture.length === 0) return false
  const parts = gesture.split('+')
  const key = parts[parts.length - 1].toLowerCase()
  const needCtrl = parts.includes('Ctrl')
  const needShift = parts.includes('Shift')
  const needAlt = parts.includes('Alt')
  const ctrl = event.ctrlKey || event.metaKey
  const eventKey = event.key.length === 1 ? event.key.toLowerCase() : event.key.toLowerCase()
  const normalized = key.startsWith('d') && key.length === 2 ? key.slice(1) : key
  const aliases = KEY_ALIASES[normalized]
  // Shift is part of typing `+` on most layouts, so a gesture that names a
  // shifted character cannot also demand that Shift was not held.
  const shiftMatches = aliases ? true : event.shiftKey === needShift
  return (
    ctrl === needCtrl &&
    shiftMatches &&
    event.altKey === needAlt &&
    (aliases ? aliases.includes(eventKey) : eventKey === normalized)
  )
}

/** Descriptor before the active gesture is resolved from persisted overrides. */
type HotkeyDef = Omit<HotkeyAction, 'gesture'>

/**
 * Every command as a bindable descriptor, with the writer's own gestures folded
 * over the factory defaults.
 *
 * The list used to be the other way round: an action existed *because* it had a
 * hotkey, which is why the command palette - built from this same list - could
 * only ever offer the two dozen commands somebody had thought to bind. A
 * gesture is a property of a command now, and most commands have none until
 * the writer gives them one in Settings.
 */
export function buildDefaultHotkeys(): HotkeyAction[] {
  const defs: HotkeyDef[] = COMMANDS.map((command) => ({
    actionId: command.id,
    defaultGesture: command.defaultGesture ?? '',
    categoryKey: command.categoryKey,
    labelKey: command.labelKey,
    run: command.run
  }))
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
let hotkeysEnabled = true

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
  metaKey?: boolean
  shiftKey: boolean
  altKey: boolean
}): boolean {
  if (!hotkeysEnabled) return false
  const synthetic = {
    key: payload.key,
    code: payload.code,
    ctrlKey: payload.ctrlKey,
    // Cmd must survive the trip: matchGesture reads Ctrl as "ctrlKey || metaKey",
    // so dropping it here silently disabled every Cmd shortcut typed in the editor.
    metaKey: payload.metaKey === true,
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

/** Temporarily blocks both shell and iframe-forwarded command shortcuts. */
export function setHotkeysEnabled(enabled: boolean): void {
  hotkeysEnabled = enabled
}

export function installHotkeys(actions: HotkeyAction[]): () => void {
  installedActions = actions
  const onKeyDown = (event: KeyboardEvent): void => {
    if (!hotkeysEnabled) return
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
