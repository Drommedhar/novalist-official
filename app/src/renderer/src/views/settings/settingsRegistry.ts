import { SETTINGS_SECTION_KEYS, type SettingsSectionKey, type SettingsControlMetadata, type SettingsSectionMetadata } from './settingsMetadata'
import { SETTINGS_REGISTRY } from './settingsSections'

const sectionKeys = new Set<string>(SETTINGS_SECTION_KEYS)

export function isSettingsSectionKey(value: string): value is SettingsSectionKey {
  return sectionKeys.has(value)
}

export function settingsSectionsForContext(context: {
  hasProject: boolean
  isMobile: boolean
}): readonly SettingsSectionMetadata[] {
  return SETTINGS_REGISTRY.filter(
    (section) =>
      (!section.requiresProject || context.hasProject) &&
      (!section.desktopOnly || !context.isMobile) &&
      (!section.mobileOnly || context.isMobile)
  )
}

function normalized(value: string): string {
  return value
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase()
    .trim()
}

function translatedText(
  keys: readonly string[] | undefined,
  translate: (key: string) => string
): string[] {
  return keys?.map((key) => translate(key)) ?? []
}

export interface SettingsSearchResult {
  section: SettingsSectionMetadata
  control?: SettingsControlMetadata
}

/** Searches both translated visible copy and language-neutral fallback terms. */
export function searchSettings(
  sections: readonly SettingsSectionMetadata[],
  query: string,
  translate: (key: string) => string
): SettingsSearchResult[] {
  const needle = normalized(query)
  if (!needle) return []

  const results: SettingsSearchResult[] = []
  for (const section of sections) {
    const sectionText = [translate(section.titleKey), ...(section.keywords ?? [])]
      .map(normalized)
      .join(' ')
    if (sectionText.includes(needle)) results.push({ section })

    for (const item of section.controls ?? []) {
      const controlText = [
        translate(item.labelKey),
        ...translatedText(item.descriptionKeys, translate),
        ...(item.keywords ?? [])
      ]
        .map(normalized)
        .join(' ')
      if (controlText.includes(needle)) results.push({ section, control: item })
    }
  }
  return results
}

export function settingsSection(key: SettingsSectionKey): SettingsSectionMetadata {
  return SETTINGS_REGISTRY.find((section) => section.key === key)!
}

export function settingsControl(
  sectionKey: SettingsSectionKey,
  controlKey: string
): SettingsControlMetadata | undefined {
  return settingsSection(sectionKey).controls?.find((item) => item.key === controlKey)
}

export * from './settingsMetadata'
export { SETTINGS_REGISTRY } from './settingsSections'
