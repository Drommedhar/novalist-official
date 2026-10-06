import { ChevronLeft } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { MobileGroup, MobileNav, MobileRow, useMobileNav } from '../../shell/MobileNav'
import { type SettingsLayoutProps } from './settingsLayoutTypes'
import {
  SETTINGS_CATEGORIES
} from './settingsRegistry'
import { ResolvedSection } from './settingsViewTypes'
function SettingsPhoneRow({ section }: { section: ResolvedSection }): React.JSX.Element {
  const { t } = useTranslation()
  const nav = useMobileNav()
  const title = t(section.titleKey)
  return <MobileRow label={title} onClick={() => nav.push({ id: section.key, title })} />
}
export function SettingsPhone(props: SettingsLayoutProps): React.JSX.Element {
  const { t, searchResults, query, visibleSections, projectLoaded, projectName, setMainView, search, setSearch, closeProjectFromSettings } = props
  const resultKeys = new Set(searchResults.map((result) => result.section.key))
  const shown = query
    ? visibleSections.filter((section) => resultKeys.has(section.key))
    : visibleSections
  const grouped = SETTINGS_CATEGORIES.map((category) => ({
    id: category,
    // The open project is a group of its own at the top of the index rather
    // than a page to drill into - closing is one press, not a row that opens
    // a screen holding one button. Under a search it stays an ordinary
    // result, so "close" still finds it.
    sections: shown.filter(
      (section) => section.category === category && section.key !== 'project'
    )
  })).filter((group) => group.sections.length > 0)
  return (
    <MobileNav
      title={t('settings.title')}
      // Resolved out of this render's sections, so an open section shows the
      // value the setting has now. The search filter is deliberately not
      // consulted: typing in the index must not blank the page on top of it.
      renderPage={(key) => {
        const section = visibleSections.find((candidate) => candidate.key === key)
        return section ? <div className="settings-phone-section">{section.body}</div> : null
      }}
    >
      <div className="settings-phone">
        {/* Settings is the one screen a writer can open before a project, and
              on a phone there is nothing around it then - no rail, and the
              native tab bar stays hidden until a project is open. Without this
              the welcome screen was a one-way door. Inside a project the tab
              bar is the way out, so the button is not there. */}
        {!projectLoaded && (
          <button
            type="button"
            className="mobile-nav-back settings-phone-back"
            onClick={() => setMainView('dashboard')}
          >
            <ChevronLeft size={20} strokeWidth={2} />
            <span className="mobile-nav-back-label">Novalist</span>
          </button>
        )}
        <h1 className="mobile-nav-title settings-phone-title">{t('settings.title')}</h1>
        <input
          className="dialog-input settings-phone-search"
          placeholder={t('settings.searchPlaceholder')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        {/* The project, and the way back out of it. iOS states what is open
              and offers the one action on it; there is no other way to leave a
              project on a phone - no menu bar, and the tab bar only moves
              between screens inside it. */}
        {!query && projectLoaded && (
          <MobileGroup header={projectName ?? undefined} footer={t('settings.closeProjectDesc')}>
            <MobileRow
              label={t('command.closeProject')}
              variant="action"
              onClick={() => void closeProjectFromSettings()}
            />
          </MobileGroup>
        )}
        {query ? (
          <MobileGroup>
            {shown.map((s) => (
              <SettingsPhoneRow key={s.key} section={s} />
            ))}
          </MobileGroup>
        ) : (
          <>
            {grouped.map((group) => (
              <MobileGroup key={group.id} header={t(`settings.group.${group.id}`)}>
                {group.sections.map((s) => (
                  <SettingsPhoneRow key={s.key} section={s} />
                ))}
              </MobileGroup>
            ))}
          </>
        )}
      </div>
    </MobileNav>
  )
}
