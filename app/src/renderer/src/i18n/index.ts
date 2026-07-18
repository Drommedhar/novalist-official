import i18next from 'i18next'
import { initReactI18next } from 'react-i18next'

// Locale files use flat dotted keys ("shell.binder"), carried over unchanged
// from the Avalonia app. keySeparator stays off so dots are part of the key.
const localeModules = import.meta.glob<{ default: Record<string, unknown> }>(
  '../locales/*.json',
  { eager: true }
)

const resources: Record<string, { translation: Record<string, string> }> = {}
/** Unflattened locale JSON per language, for consumers that need arrays
 * (e.g. relationship role keywords aggregated across all languages). */
export const rawLocales: Record<string, Record<string, unknown>> = {}
for (const [path, module] of Object.entries(localeModules)) {
  const lang = path.replace(/^.*\//, '').replace(/\.json$/, '')
  resources[lang] = { translation: flatten(module.default) }
  rawLocales[lang] = module.default
}

/** Union of a relationships.* keyword array across every bundled language,
 * mirroring the Avalonia RelationshipRoles aggregator. */
export function relationshipRoleKeywords(kind: string): Set<string> {
  const keywords = new Set<string>()
  for (const locale of Object.values(rawLocales)) {
    const section = locale['relationships'] as Record<string, unknown> | undefined
    const list = section?.[kind]
    if (Array.isArray(list)) for (const word of list) keywords.add(String(word).toLowerCase())
  }
  return keywords
}

/** Locale JSON nests by namespace; flatten to the dotted keys the app uses. */
export function flatten(
  value: Record<string, unknown>,
  prefix = '',
  out: Record<string, string> = {}
): Record<string, string> {
  for (const [key, child] of Object.entries(value)) {
    const full = prefix ? `${prefix}.${key}` : key
    if (typeof child === 'object' && child !== null && !Array.isArray(child)) {
      flatten(child as Record<string, unknown>, full, out)
    } else {
      out[full] = String(child)
    }
  }
  return out
}

export function availableLanguages(): { code: string; name: string }[] {
  return Object.entries(resources).map(([code, res]) => ({
    code,
    name: res.translation['language.name'] ?? code
  }))
}

void i18next.use(initReactI18next).init({
  resources,
  lng: navigator.language.startsWith('de') ? 'de' : navigator.language.startsWith('zh') ? 'zh-CN' : 'en',
  fallbackLng: 'en',
  keySeparator: false,
  nsSeparator: false,
  interpolation: { escapeValue: false }
})

export default i18next
