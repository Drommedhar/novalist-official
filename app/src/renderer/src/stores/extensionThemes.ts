/**
 * Applies an extension-contributed theme (IThemeContributor) on the Electron
 * host. Only the portable ThemeOverride.AccentColor is meaningful here — the
 * Avalonia Styles/ResourcePath forms cannot be rendered — so a contributed theme
 * is injected as a CSS-variable override of the accent tokens. The selection is
 * persisted so it survives reloads.
 */

const STORAGE_KEY = 'nl.extensionTheme'

/** Applies (or clears, when accentColor is null) the accent override and
 * remembers the selected theme name. */
export function applyExtensionTheme(name: string | null, accentColor: string | null): void {
  const root = document.documentElement
  if (accentColor) {
    root.style.setProperty('--nl-accent', accentColor)
    root.style.setProperty('--nl-accent-hover', accentColor)
  } else {
    root.style.removeProperty('--nl-accent')
    root.style.removeProperty('--nl-accent-hover')
  }
  if (name) localStorage.setItem(STORAGE_KEY, name)
  else localStorage.removeItem(STORAGE_KEY)
}

/** The persisted extension theme name, or null. */
export function selectedExtensionTheme(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY)
  } catch {
    return null
  }
}
