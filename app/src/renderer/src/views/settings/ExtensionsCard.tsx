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
 * Contributed pages, schemas and wizards are settings. Installation and
 * lifecycle controls belong to the linked Extensions view.
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
