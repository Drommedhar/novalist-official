import i18next from 'i18next'
import { initReactI18next } from 'react-i18next'

// Locale files use flat dotted keys ("shell.binder"), carried over unchanged
// from the Avalonia app. keySeparator stays off so dots are part of the key.
const localeModules = import.meta.glob<{ default: Record<string, unknown> }>(
  '../locales/*.json',
  { eager: true }
)

const resources: Record<string, { translation: Record<string, string> }> = {}
for (const [path, module] of Object.entries(localeModules)) {
  const lang = path.replace(/^.*\//, '').replace(/\.json$/, '')
  resources[lang] = { translation: flatten(module.default) }
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
