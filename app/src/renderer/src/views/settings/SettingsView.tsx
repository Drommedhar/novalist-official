import { ArrowLeft, Search, X } from 'lucide-react'
import { type SettingsSection } from '../../stores/settingsStore'
import './settings.css'
import { SettingsFields } from './SettingsFields'
import { SettingsPhone } from './SettingsLayouts'
import { type SettingsLayoutProps } from './settingsLayoutTypes'
import {
  setSettingsDestination
} from './settingsNavigation'
import {
  SETTINGS_CATEGORIES,
  type SettingsScopeKind,
  type SettingsSectionKey,
  type SettingsSectionMetadata
} from './settingsRegistry'
import { settingsSectionBodies } from './settingsSectionBodies'
import { ACCESSIBLE_FONTS, SYSTEM_FONTS } from './settingsTypography'
import { ResolvedSection, Scope, SectionBodyDef } from './settingsViewTypes'
import { useSettingsModel } from './useSettingsModel'
import { useContentTransition } from '../../shell/useContentTransition'

export function SettingsView(): React.JSX.Element {
  const {
    t, view, update, pinSection,
    clearSection, updateProjectMeta, themes, assetDirs,
    isMobile, isPhone, projectLoaded, projectName,
    setMainView, uiScale, setUiScale, resetUiScale,
    tipsEnabled, setTipsEnabled, voices, systemVoices,
    writingLanguages, displayInfo, displayInfoBusy, refreshDisplayInfo,
    closeProjectFromSettings, destination, search, setSearch,
    selectedSection, setSelectedSection, sectionSurfaceRef, sectionsRef,
    availableMetadata, searchResults
  } = useSettingsModel()

  if (!view) return <div className="main-placeholder">{t('shell.backendConnecting')}</div>

  const eff = view.effective
  // The language the prose is in, which is what read-aloud speaks and therefore
  // what the voice has to match.
  const writingLanguage = eff.autoReplacementLanguage || 'en'
  const project = view.project

  /** Whether the open project overrides a section. Read from what is stored, so
   * the switch survives leaving and re-entering Settings. */
  const isOverridden = (section: SettingsSection): boolean =>
    view.overriddenSections?.[section] === true

  /**
   * The per-section Global / This-project switch. Ticking pins the values in
   * effect now to the project, so the override exists from that moment rather
   * than only once a field is edited; unticking drops it and the section falls
   * back to the global values.
   */
  const scopeToggle = (section: SettingsSection): React.JSX.Element | null => {
    if (!view.hasProject) return null
    const overridden = isOverridden(section)
    return (
      <>
        <label className="relationships-toggle settings-scope">
          <input
            type="checkbox"
            checked={overridden}
            onChange={(e) => {
              void (e.target.checked ? pinSection(section) : clearSection(section))
            }}
          />
          {t('settings.scopeProjectOverride')}
        </label>
        <p className="settings-hint settings-scope-hint">
          {t(overridden ? 'settings.scopeEditingProject' : 'settings.scopeEditingGlobal')}
        </p>
      </>
    )
  }

  /** Where an edit in a section is written. With no project open, or with the
   * section not overridden, edits go to the global defaults. */
  const scopeFor = (section: SettingsSection): Scope =>
    view.hasProject && isOverridden(section) ? 'project' : 'global'

  const fontDatalist = (
    <datalist id="settings-fonts">
      {[...SYSTEM_FONTS, ...ACCESSIBLE_FONTS].map((f) => (
        <option key={f} value={f} />
      ))}
    </datalist>
  )

  const sectionBodies: SectionBodyDef[] = settingsSectionBodies({
    projectName, t, closeProjectFromSettings, scopeToggle,
    eff, update, scopeFor, themes,
    uiScale, setUiScale, resetUiScale, assetDirs,
    fontDatalist, systemVoices, voices, writingLanguage,
    isMobile, tipsEnabled, setTipsEnabled, view,
    project, updateProjectMeta, writingLanguages, displayInfoBusy,
    refreshDisplayInfo, displayInfo
  })

  const bodiesByKey = new Map(sectionBodies.map((section) => [section.key, section]))
  const visibleSections: ResolvedSection[] = availableMetadata.flatMap((metadata) => {
    const body = bodiesByKey.get(metadata.key)
    return body
      ? [{ ...metadata, ...body, standalone: metadata.standalone ?? body.standalone }]
      : []
  })
  const activeSection =
    visibleSections.find((section) => section.key === selectedSection) ?? visibleSections[0]
  const query = search.trim()

  const goTo = (section: SettingsSectionKey, control?: string): void => {
    setSearch('')
    setSelectedSection(section)
    setSettingsDestination({ section, control, origin: destination.origin })
  }

  const sectionOverride = (section: SettingsSectionKey): SettingsSection | null => {
    if (section === 'appearance' || section === 'editor') return section
    if (section === 'writingAssistance') return 'writing'
    return null
  }

  const overrideActive = (section: SettingsSectionMetadata): boolean => {
    const override = sectionOverride(section.key)
    return override ? isOverridden(override) : false
  }

  const returnToOrigin = (): void => {
    if (!destination.origin) return
    const origin = destination.origin
    setSettingsDestination({ section: activeSection?.key ?? 'appearance' })
    setMainView(origin.view)
  }

  // Phone navigation keeps the native-style drilldown, but its groups and
  // availability now come from the same registry as desktop.
  const layout = {
    t, destination, returnToOrigin, search,
    setSearch, query, visibleSections, activeSection,
    goTo, sectionsRef, searchResults, sectionSurfaceRef,
    overrideActive, projectLoaded, projectName, setMainView,
    closeProjectFromSettings
  }
  return isPhone ? <SettingsPhone {...layout} /> : <SettingsDesktop {...layout} />
}

function scopeLabelKey(scope: SettingsScopeKind, projectOverride: boolean): string {
  if (scope === 'project') return 'settings.scopeProjectBadge'
  if (scope === 'mixed') return 'settings.scopeMixedBadge'
  if (scope === 'overridable') {
    return projectOverride
      ? 'settings.scopeOverrideProjectBadge'
      : 'settings.scopeOverrideGlobalBadge'
  }
  return 'settings.scopeGlobalBadge'
}
function SettingsDesktop(props: SettingsLayoutProps): React.JSX.Element {
  const {
    t, destination, returnToOrigin, search,
    setSearch, query, visibleSections, activeSection,
    goTo, sectionsRef, searchResults, sectionSurfaceRef,
    overrideActive
  } = props
  useContentTransition(sectionsRef, `${activeSection?.key ?? ''}:${Boolean(query)}`)
  return (
    <div className="dashboard settings-view">
      <div className="settings-header">
        <div className="settings-heading-copy">
          {destination.origin && (
            <button className="settings-origin" onClick={returnToOrigin}>
              <ArrowLeft size={16} aria-hidden="true" />
              {t('settings.backTo', { context: t(destination.origin.labelKey) })}
            </button>
          )}
          <h1 className="dashboard-title">{t('settings.title')}</h1>
        </div>
        <div className="settings-search-box">
          <Search size={16} aria-hidden="true" />
          <input
            className="settings-search"
            type="search"
            aria-label={t('settings.searchPlaceholder')}
            placeholder={t('settings.searchPlaceholder')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          {query && (
            <button
              className="settings-search-clear"
              aria-label={t('settings.searchClear')}
              onClick={() => setSearch('')}
            >
              <X size={16} aria-hidden="true" />
            </button>
          )}
        </div>
      </div>

      <div className="settings-layout">
        <nav className="settings-nav" aria-label={t('settings.navigationLabel')}>
          {SETTINGS_CATEGORIES.map((category) => {
            const categorySections = visibleSections.filter(
              (section) => section.category === category
            )
            if (categorySections.length === 0) return null
            return (
              <div className="settings-nav-group" key={category}>
                <div className="settings-nav-heading">{t(`settings.group.${category}`)}</div>
                {categorySections.map((section) => (
                  <button
                    key={section.key}
                    className={`settings-nav-item${activeSection?.key === section.key ? ' active' : ''
                      }`}
                    aria-current={activeSection?.key === section.key ? 'page' : undefined}
                    onClick={() => goTo(section.key)}
                  >
                    {t(section.titleKey)}
                  </button>
                ))}
              </div>
            )
          })}
        </nav>

        <div className="settings-sections" ref={sectionsRef}>
          {query ? (
            <section className="settings-results" aria-live="polite">
              <h2 className="settings-results-title">
                {t('settings.searchResults', { count: searchResults.length })}
              </h2>
              {searchResults.length === 0 ? (
                <p className="settings-results-empty">{t('settings.searchNoResults')}</p>
              ) : (
                <div className="settings-results-list">
                  {searchResults.map((result) => {
                    const resultKey = `${result.section.key}:${result.control?.key ?? 'section'}`
                    const resultTitle = result.control
                      ? t(result.control.labelKey)
                      : t(result.section.titleKey)
                    return (
                      <button
                        className="settings-result"
                        key={resultKey}
                        onClick={() => goTo(result.section.key, result.control?.key)}
                      >
                        <span className="settings-result-title">{resultTitle}</span>
                        <span className="settings-result-context">
                          {t(result.section.titleKey)} ·{' '}
                          {t(`settings.group.${result.section.category}`)}
                        </span>
                      </button>
                    )
                  })}
                </div>
              )}
            </section>
          ) : activeSection ? (
            <section
              key={activeSection.key}
              ref={sectionSurfaceRef}
              className="settings-section-surface"
              data-settings-section={activeSection.key}
              tabIndex={-1}
            >
              <header className="settings-section-header">
                <div>
                  <div className="settings-section-category">
                    {t(`settings.group.${activeSection.category}`)}
                  </div>
                  <h2 className="settings-section-title">{t(activeSection.titleKey)}</h2>
                </div>
                <span className={`settings-scope-badge ${activeSection.scope}`}>
                  {t(scopeLabelKey(activeSection.scope, overrideActive(activeSection)))}
                </span>
              </header>
              {activeSection.standalone ? (
                <div className="settings-standalone">{activeSection.body}</div>
              ) : (
                <div className="dashboard-card export-card">
                  <SettingsFields>{activeSection.body}</SettingsFields>
                </div>
              )}
            </section>
          ) : null}
        </div>
      </div>
    </div>
  )
}
