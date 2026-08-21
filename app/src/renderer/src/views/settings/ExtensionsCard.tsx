import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { ExtensionSettings } from '../extensions/ExtensionSettings'
import { useExtensionsStore } from '../../stores/extensionsStore'
import { useShellStore } from '../../stores/shellStore'
import {
  extensionsAvailable,
  ExtensionsUnavailable
} from '../extensions/ExtensionsUnavailable'

/**
 * Everything installed extensions have put into Settings.
 *
 * This used to be a read-only list of what was installed - a fraction of the
 * Extensions view, offering nothing you could not see better over there, while
 * the settings extensions actually contribute lived in the Extensions view
 * itself. Both halves were in the wrong place: what an extension can be
 * configured to do is a setting, and settings live in Settings; installing,
 * enabling and removing one is managing the application, and that is what the
 * Extensions view is for.
 *
 * So the contributed pages, schemas and wizards moved here, and what is left of
 * the list is a line saying how many extensions are installed with a way over
 * to manage them.
 */
export function ExtensionsCard(): React.JSX.Element {
  const { t } = useTranslation()
  const extensions = useExtensionsStore((s) => s.extensions)
  const load = useExtensionsStore((s) => s.load)
  const enabled = extensions.filter((e) => e.isEnabled).length
  const available = extensionsAvailable()

  useEffect(() => {
    if (!available) return
    void load()
  }, [available, load])

  // The App Store build has no extension feature, so there is nothing to count
  // and no contributed settings to render - only the explanation, which belongs
  // here because Settings is where somebody looks for it.
  if (!available) {
    return (
      <section className="dashboard-card export-card">
        <div className="dashboard-card-title">{t('extensions.title')}</div>
        <ExtensionsUnavailable />
      </section>
    )
  }

  return (
    <>
      <section className="dashboard-card export-card">
        <div className="dashboard-card-title">{t('extensions.title')}</div>
        {extensions.length === 0 ? (
          <p className="codex-empty">{t('extensions.noExtensions')}</p>
        ) : (
          <p className="settings-hint">
            {t('settings.extensionsInstalled', { count: extensions.length, enabled })}
          </p>
        )}
        <button
          className="btn-secondary"
          onClick={() => useShellStore.getState().setMainView('extensions')}
        >
          {t('settings.extensionsManage')}
        </button>
      </section>
      <ExtensionSettings />
    </>
  )
}
