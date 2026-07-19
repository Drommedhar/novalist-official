import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useExtensionsStore } from '../../stores/extensionsStore'

/**
 * Read-only listing of installed extensions. The extension host does not yet
 * contribute dedicated settings pages, so this surfaces what is installed plus
 * each extension's enabled state and any load error.
 */
export function ExtensionsCard(): React.JSX.Element {
  const { t } = useTranslation()
  const extensions = useExtensionsStore((s) => s.extensions)
  const load = useExtensionsStore((s) => s.load)

  useEffect(() => {
    void load()
  }, [load])

  return (
    <section className="dashboard-card export-card">
      <div className="dashboard-card-title">{t('extensions.title')}</div>
      {extensions.length === 0 ? (
        <p className="codex-empty">{t('extensions.noExtensions')}</p>
      ) : (
        <div className="settings-extension-list">
          {extensions.map((ext) => (
            <div key={ext.id} className="settings-extension-row">
              <div className="settings-extension-info">
                <span className="settings-extension-name">{ext.name}</span>
                <span className="settings-extension-meta">
                  {t('extensions.version')} {ext.version}
                  {ext.loadError ? ` · ${t('extensions.loadError')}` : ''}
                </span>
              </div>
              <span
                className={`settings-extension-state${ext.isEnabled ? ' on' : ''}`}
              >
                {ext.isEnabled ? t('extensions.enabled') : t('extensions.disabled')}
              </span>
            </div>
          ))}
        </div>
      )}
    </section>
  )
}
