import { create } from 'zustand'

/**
 * The set of themes the user can choose from, and the machinery that applies
 * one. Three sources feed it:
 *
 *  - built-in themes, whose palettes live in tokens.css keyed by [data-theme];
 *  - folder themes, dropped into the user's Themes folder (`appearance/themes`);
 *  - extension themes, contributed through IThemeContributor (`extensions/themes`).
 *
 * The last two arrive as data - a map of --nl-* tokens, a stylesheet, or both -
 * so they are applied by generating CSS at runtime rather than by shipping it.
 *
 * Specificity note: generated token rules use `:root:where([data-theme='...'])`,
 * which scores the same as the plain `:root` base block in tokens.css but sits
 * later in the document, so it wins; and it scores below the
 * `[data-material]` layer, so native vibrancy still overrides chrome surfaces
 * exactly as it does for the built-in themes.
 */

export type ThemeOrigin = 'built-in' | 'folder' | 'extension'

export interface CatalogTheme {
  /** Display name, as shown in the theme dropdown and stored in settings. */
  name: string
  /** Value written to `data-theme` on the root element. */
  slug: string
  /** Design-token overrides, already validated by the backend. */
  tokens: Record<string, string>
  /** Stylesheet injected only while this theme is selected. */
  css: string | null
  origin: ThemeOrigin
}

/** Themes whose palettes ship in tokens.css. Order is the dropdown order. */
const BUILT_IN: CatalogTheme[] = [
  { name: 'Default', slug: 'dark', tokens: {}, css: null, origin: 'built-in' },
  { name: 'Discord', slug: 'discord', tokens: {}, css: null, origin: 'built-in' },
  {
    name: 'High Contrast',
    slug: 'high-contrast',
    tokens: {},
    css: null,
    origin: 'built-in'
  },
  {
    name: 'Catppuccin Mocha',
    slug: 'catppuccin-mocha',
    tokens: {},
    css: null,
    origin: 'built-in'
  }
]

const TOKENS_STYLE_ID = 'nl-dynamic-theme-tokens'
const CSS_STYLE_ID = 'nl-dynamic-theme-css'

interface ThemeCatalogState {
  themes: CatalogTheme[]
  /** Replaces the themes from one source and re-applies the current selection,
   * so a theme that arrives after settings load still takes effect. */
  setSource(origin: Exclude<ThemeOrigin, 'built-in'>, themes: CatalogTheme[]): void
  /** Repaints with whatever is currently selected. Used after the folders are
   *  re-read, so an edit to the theme in use shows without switching away. */
  reapply(): void
}

/** The selection last passed to applyTheme, replayed when the catalog changes. */
let currentName = 'Default'
let currentAccent: string | null = null

function styleElement(id: string): HTMLStyleElement {
  const existing = document.getElementById(id)
  if (existing instanceof HTMLStyleElement) return existing
  const created = document.createElement('style')
  created.id = id
  document.head.appendChild(created)
  return created
}

/** One `:root:where(...)` rule per theme that declares tokens. */
function buildTokenSheet(themes: CatalogTheme[]): string {
  return themes
    .filter((theme) => Object.keys(theme.tokens).length > 0)
    .map((theme) => {
      const body = Object.entries(theme.tokens)
        .map(([token, value]) => `  ${token}: ${value};`)
        .join('\n')
      return `:root:where([data-theme='${theme.slug}']) {\n${body}\n}`
    })
    .join('\n\n')
}

export const useThemeCatalog = create<ThemeCatalogState>((set, get) => ({
  themes: BUILT_IN,

  reapply: () => applyTheme(currentName, currentAccent),
  setSource: (origin, themes) => {
    const rest = get().themes.filter((t) => t.origin !== origin)
    // Built-ins first, then folder themes, then extension themes - the order the
    // dropdown reads best in, and stable as sources arrive at different times.
    const merged = [...rest, ...themes].sort(
      (a, b) => originRank(a.origin) - originRank(b.origin)
    )
    set({ themes: merged })
    styleElement(TOKENS_STYLE_ID).textContent = buildTokenSheet(merged)
    applyTheme(currentName, currentAccent)
  }
}))

function originRank(origin: ThemeOrigin): number {
  return origin === 'built-in' ? 0 : origin === 'folder' ? 1 : 2
}

/** The catalog entry for a stored theme name, or undefined when the theme it
 * names is gone (its extension was disabled, its file was deleted). */
export function findTheme(name: string): CatalogTheme | undefined {
  return useThemeCatalog.getState().themes.find((t) => t.name === name)
}

/** Every theme name, for the Settings dropdown. */
export function themeNames(): CatalogTheme[] {
  return useThemeCatalog.getState().themes
}

/**
 * Applies a theme by name plus an optional accent override. An unknown name
 * falls back to the default palette rather than leaving the app unstyled, which
 * is what happens between startup and the arrival of extension themes, and
 * permanently if the user deletes a theme file they had selected.
 */
export function applyTheme(name: string, accentColor: string | null): void {
  currentName = name
  currentAccent = accentColor

  const theme = findTheme(name)
  const root = document.documentElement
  root.dataset.theme = theme?.slug ?? 'dark'

  // A stylesheet theme is injected only while selected, so its rules - which can
  // be anything, not just token declarations - never leak into another theme.
  styleElement(CSS_STYLE_ID).textContent = theme?.css ?? ''

  if (accentColor) {
    root.style.setProperty('--nl-accent', accentColor)
    root.style.setProperty('--nl-accent-hover', accentColor)
  } else {
    root.style.removeProperty('--nl-accent')
    root.style.removeProperty('--nl-accent-hover')
  }

  syncTitleBarColors()
}

/**
 * Repaints the system-drawn window controls to match the theme. On Windows and
 * Linux the title bar is hidden and the app's own toolbar stands in for it, so
 * the minimise/maximise/close buttons have to be told the toolbar's colours or
 * they stay light grey against a dark strip. Reads the tokens back off the
 * document so a theme or custom accent needs no separate colour table.
 */
function syncTitleBarColors(): void {
  if (typeof window.novalist?.setTitleBarColors !== 'function') return
  const style = getComputedStyle(document.documentElement)
  const color = style.getPropertyValue('--nl-surface-toolbar').trim()
  const symbol = style.getPropertyValue('--nl-text').trim()
  // The overlay API takes only opaque colours; under the macOS material layer
  // the toolbar token resolves to a translucent value, and that path has native
  // traffic lights anyway, so there is nothing to repaint.
  if (!color.startsWith('#') || !symbol.startsWith('#')) return
  window.novalist.setTitleBarColors(color, symbol)
}
