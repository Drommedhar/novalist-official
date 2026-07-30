import { rpc } from '../rpc/client'
import { registerUserLocales } from '../i18n'
import { useThemeCatalog, type CatalogTheme } from './themeCatalog'

/**
 * Loads the assets the user dropped into their Themes and Locales folders and
 * registers them before settings are applied, so a saved custom theme or
 * language is in place on the first paint instead of after a flash of the
 * default.
 *
 * The folders are also watched: the backend sends `appearance/assetsChanged`
 * when one of them is touched, and the assets are re-read without a restart.
 * Reading them once per launch made iterating on a theme a relaunch-per-edit
 * loop, which is the wrong loop for something anybody tunes by eye.
 */

interface UserThemeDto {
  name: string
  slug: string
  tokens: Record<string, string>
  css: string | null
}

interface UserLocaleDto {
  code: string
  name: string
  json: string
}

export interface AppearanceDirectories {
  themes: string
  locales: string
  analysis: string
}

let directories: AppearanceDirectories | null = null

/** The folders the user drops assets into, once loadUserAssets has run. */
export function assetDirectories(): AppearanceDirectories | null {
  return directories
}

/**
 * Fetches user themes and locales and registers both. Never throws: a backend
 * that cannot list the folders costs the user their custom assets, not their
 * app, so every step degrades to "none found".
 */
export async function loadUserAssets(): Promise<void> {
  const [themes, locales, dirs] = await Promise.all([
    rpc.request<UserThemeDto[]>('appearance/themes').catch(() => []),
    rpc.request<UserLocaleDto[]>('appearance/locales').catch(() => []),
    rpc.request<AppearanceDirectories>('appearance/directories').catch(() => null)
  ])

  directories = dirs

  const catalog: CatalogTheme[] = themes.map((t) => ({
    name: t.name,
    slug: t.slug,
    tokens: t.tokens ?? {},
    css: t.css,
    origin: 'folder'
  }))
  useThemeCatalog.getState().setSource('folder', catalog)

  registerUserLocales(
    locales.flatMap((locale) => {
      const parsed = parseLocale(locale.json)
      return parsed ? [{ code: locale.code, name: locale.name, translation: parsed }] : []
    })
  )
}

/**
 * Re-reads the asset folders whenever the backend says one changed, and
 * re-applies the current theme so an edit to the selected one is visible at
 * once rather than after switching away and back.
 *
 * Registered once, at startup, before the first load.
 */
export function watchUserAssets(): void {
  rpc.onNotification('appearance/assetsChanged', () => {
    void loadUserAssets().then(() => useThemeCatalog.getState().reapply())
  })
}

/** A locale file the backend already validated as JSON; parsed defensively all
 * the same, because one unusable file must not cost the user the others. */
function parseLocale(json: string): Record<string, unknown> | null {
  try {
    const parsed: unknown = JSON.parse(json)
    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null
  } catch {
    return null
  }
}
