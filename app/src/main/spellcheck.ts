import { ipcMain, BrowserWindow, Menu, session, type ContextMenuParams, type WebContents } from 'electron'

/**
 * The platform's own spell checker, driving the red underlines in the prose
 * surface.
 *
 * This is the offline half of Novalist's proofing: LanguageTool needs a server
 * and gives grammar, this needs neither and gives spelling. On macOS it is the
 * system checker, fully local. Elsewhere Chromium loads a Hunspell dictionary,
 * fetched once per language and cached in the user data folder from then on.
 */

/** Chromium wants full locale tags. A bare writing language ("de") has to become
 *  one ("de-DE") or the dictionary silently never loads. */
const REGION_FOR: Record<string, string> = {
  en: 'en-US',
  de: 'de-DE',
  fr: 'fr-FR',
  es: 'es-ES',
  it: 'it-IT',
  pt: 'pt-BR',
  nl: 'nl-NL',
  pl: 'pl-PL',
  ru: 'ru-RU',
  sv: 'sv-SE',
  da: 'da-DK',
  nb: 'nb-NO',
  fi: 'fi-FI',
  cs: 'cs-CZ',
  tr: 'tr-TR',
  el: 'el-GR',
  ro: 'ro-RO',
  hu: 'hu-HU',
  uk: 'uk-UA',
  ko: 'ko',
  vi: 'vi',
  fa: 'fa',
  he: 'he',
  hi: 'hi',
  id: 'id',
  ca: 'ca',
  hr: 'hr',
  sr: 'sr',
  sk: 'sk',
  bg: 'bg',
  et: 'et',
  lt: 'lt',
  lv: 'lv',
  sq: 'sq',
  hy: 'hy',
  ta: 'ta'
}

export function toChromiumLocale(tag: string): string {
  const trimmed = tag.trim()
  if (trimmed.length === 0) return 'en-US'
  if (trimmed.includes('-')) return trimmed
  return REGION_FOR[trimmed.toLowerCase()] ?? trimmed
}

/**
 * Keeps only the languages this Chromium build actually ships a dictionary for.
 * Passing an unsupported tag makes `setSpellCheckerLanguages` throw and take
 * every other language down with it, so one bad tag must not cost the writer
 * their spell check.
 */
export function supportedLanguages(requested: string[], available: string[]): string[] {
  // macOS uses the system checker and reports no list; everything is allowed
  // through and the OS decides.
  if (available.length === 0) return requested
  const lookup = new Map(available.map((tag) => [tag.toLowerCase(), tag]))
  const kept: string[] = []
  for (const tag of requested) {
    const hit = lookup.get(tag.toLowerCase())
    if (hit && !kept.includes(hit)) kept.push(hit)
  }
  return kept
}

/** Applies the writer's spell-check settings to the session. */
export function applySpellCheck(enabled: boolean, languages: string[], words: string[]): void {
  const ses = session.defaultSession

  if (!enabled) {
    // An empty language list is how Chromium is told to stop checking.
    ses.setSpellCheckerEnabled(false)
    return
  }

  ses.setSpellCheckerEnabled(true)
  const wanted = languages.map(toChromiumLocale)
  const usable = supportedLanguages(wanted, ses.availableSpellCheckerLanguages)
  if (usable.length > 0) ses.setSpellCheckerLanguages(usable)

  for (const word of words) ses.addWordToSpellCheckerDictionary(word)
}

/**
 * The right-click menu over a misspelling: the corrections Chromium offers, and
 * a way to teach it a word. Only shown when there is actually a misspelled word
 * under the pointer, so an ordinary right-click in the prose is untouched.
 */
export function buildSpellingMenu(
  params: ContextMenuParams,
  webContents: WebContents,
  labels: { addToDictionary: string; noSuggestions: string },
  onWordAdded: (word: string) => void
): Menu | null {
  if (!params.misspelledWord) return null

  const template: Electron.MenuItemConstructorOptions[] = params.dictionarySuggestions.map(
    (suggestion) => ({
      label: suggestion,
      click: () => webContents.replaceMisspelling(suggestion)
    })
  )

  if (template.length === 0) {
    template.push({ label: labels.noSuggestions, enabled: false })
  }

  template.push({ type: 'separator' })
  template.push({
    label: labels.addToDictionary,
    click: () => {
      session.defaultSession.addWordToSpellCheckerDictionary(params.misspelledWord)
      onWordAdded(params.misspelledWord)
    }
  })

  return Menu.buildFromTemplate(template)
}

/**
 * Wires the session and the context menu. The renderer pushes settings whenever
 * they change; the main process owns the session because a BrowserWindow cannot
 * reach it from a sandboxed page.
 */
export function registerSpellCheckHandlers(): void {
  ipcMain.handle(
    'novalist:apply-spellcheck',
    (_event, enabled: boolean, languages: string[], words: string[]) => {
      applySpellCheck(enabled, languages, words)
      return session.defaultSession.availableSpellCheckerLanguages
    }
  )

  ipcMain.handle('novalist:spellcheck-languages', () =>
    session.defaultSession.availableSpellCheckerLanguages
  )
}

/**
 * Attaches the spelling context menu to a window and every frame inside it (the
 * prose lives in an iframe, and `context-menu` on the host webContents already
 * covers its subframes).
 */
export function attachSpellingMenu(
  win: BrowserWindow,
  labels: () => { addToDictionary: string; noSuggestions: string }
): void {
  win.webContents.on('context-menu', (_event, params) => {
    const menu = buildSpellingMenu(params, win.webContents, labels(), (word) => {
      win.webContents.send('novalist:spellcheck-word-added', word)
    })
    if (menu) menu.popup({ window: win })
  })
}
