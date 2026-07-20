import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FolderOpen, FolderPlus, RefreshCw } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useExtensionsStore } from '../../stores/extensionsStore'
import { useShellStore } from '../../stores/shellStore'
import { applyExtensionTheme, selectedExtensionTheme } from '../../stores/extensionThemes'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { ExtensionSettings } from './ExtensionSettings'
import { ExtensionStore } from './ExtensionStore'
import './extensions.css'

/**
 * Dedicated Extensions management view with two tabs: Installed (enable/disable,
 * uninstall, install-from-folder, extension themes + settings) and Store (browse
 * the remote gallery and install/update extensions). Mirrors the frozen desktop
 * ExtensionsView.
 */
export function ExtensionsView(): React.JSX.Element {
  const { t } = useTranslation()
  const extensions = useExtensionsStore((s) => s.extensions)
  const themes = useExtensionsStore((s) => s.themes)
  const load = useExtensionsStore((s) => s.load)
  const setEnabled = useExtensionsStore((s) => s.setEnabled)
  const install = useExtensionsStore((s) => s.install)
  const uninstall = useExtensionsStore((s) => s.uninstall)
  const storeUpdates = useExtensionsStore((s) => s.storeUpdates)
  const checkStoreUpdates = useExtensionsStore((s) => s.checkStoreUpdates)
  const installFromStore = useExtensionsStore((s) => s.installFromStore)
  const setExtView = useShellStore((s) => s.setExtView)

  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmId, setConfirmId] = useState<string | null>(null)
  const [activeTheme, setActiveTheme] = useState<string | null>(selectedExtensionTheme())
  const [tab, setTab] = useState<'installed' | 'store'>('installed')

  useEffect(() => {
    void load()
    // Populate available-update info so the Installed tab can offer updates
    // directly (no need to switch to the Store tab). Best-effort / offline-safe.
    void checkStoreUpdates().catch(() => {})
  }, [load, checkStoreUpdates])

  const doUpdate = async (id: string, repo: string): Promise<void> => {
    setBusy(true)
    try {
      await installFromStore(id, repo, true)
    } finally {
      setBusy(false)
    }
  }

  const doInstall = async (): Promise<void> => {
    const folder = await window.novalist.pickFolder(t('extensions.installFromFolder'))
    if (!folder) return
    setBusy(true)
    setError(null)
    try {
      await install(folder)
    } catch {
      setError(t('extensions.installFailed'))
    } finally {
      setBusy(false)
    }
  }

  const doToggle = async (id: string, next: boolean): Promise<void> => {
    setBusy(true)
    try {
      await setEnabled(id, next)
    } finally {
      setBusy(false)
    }
  }

  const doUninstall = async (id: string): Promise<void> => {
    setConfirmId(null)
    setBusy(true)
    try {
      // Leaving the extension's own view active would render nothing after removal.
      setExtView(null)
      await uninstall(id)
    } finally {
      setBusy(false)
    }
  }

  const confirmTarget = extensions.find((e) => e.id === confirmId)

  return (
    <div className="dashboard extensions-view">
      <div className="extensions-header">
        <h1 className="dashboard-title">{t('extensions.title')}</h1>
        {tab === 'installed' && (
        <div className="extensions-actions">
          <button
            type="button"
            className="export-inline-btn"
            onClick={() => void doInstall()}
            disabled={busy}
          >
            <FolderPlus size={13} strokeWidth={2} /> {t('extensions.installFromFolder')}
          </button>
          <button
            type="button"
            className="export-inline-btn"
            onClick={() =>
              void rpc
                .request<string>('extensions/directory')
                .then((dir) => window.novalist.revealPath(dir))
            }
            title={t('extensions.openFolder')}
          >
            <FolderOpen size={13} strokeWidth={2} /> {t('extensions.openFolder')}
          </button>
          <button
            type="button"
            className="export-inline-btn"
            onClick={() => void useExtensionsStore.getState().refreshViews()}
            title={t('extensions.reload')}
          >
            <RefreshCw size={13} strokeWidth={2} /> {t('extensions.reload')}
          </button>
        </div>
        )}
      </div>

      <div className="ext-tabs" role="tablist">
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'installed'}
          className={`ext-tab${tab === 'installed' ? ' active' : ''}`}
          onClick={() => setTab('installed')}
        >
          {t('extensions.tabInstalled')}
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'store'}
          className={`ext-tab${tab === 'store' ? ' active' : ''}`}
          onClick={() => setTab('store')}
        >
          {t('extensions.tabBrowse')}
        </button>
      </div>

      {tab === 'store' ? (
        <ExtensionStore />
      ) : (
        <>
      {error && <p className="extensions-error">{error}</p>}

      {extensions.length === 0 ? (
        <p className="codex-empty">{t('extensions.noExtensions')}</p>
      ) : (
        <div className="extensions-list">
          {extensions.map((ext) => (
            <section key={ext.id} className="dashboard-card extension-card">
              <div className="extension-card-main">
                <div className="extension-card-head">
                  <span className="extension-card-name">{ext.name}</span>
                  <span className="extension-card-version">{ext.version}</span>
                  <span
                    className={`extension-card-state${ext.isEnabled ? ' on' : ''}`}
                  >
                    {ext.isEnabled ? t('extensions.enabled') : t('extensions.disabled')}
                  </span>
                </div>
                {ext.author && (
                  <div className="extension-card-author">
                    {t('extensions.author')} {ext.author}
                  </div>
                )}
                {ext.description && (
                  <p className="extension-card-desc">{ext.description}</p>
                )}
                {ext.loadError && (
                  <p className="extension-card-loaderr">
                    {t('extensions.loadError')}: {ext.loadError}
                  </p>
                )}
              </div>
              <div className="extension-card-buttons">
                {(() => {
                  const upd = storeUpdates.find((u) => u.extensionId === ext.id)
                  return upd ? (
                    <button
                      type="button"
                      className="export-inline-btn primary"
                      disabled={busy}
                      onClick={() => void doUpdate(ext.id, upd.repo)}
                    >
                      {t('extensions.updateTo').replace('{0}', upd.availableVersion)}
                    </button>
                  ) : null
                })()}
                <button
                  type="button"
                  className="export-inline-btn"
                  disabled={busy}
                  onClick={() => void doToggle(ext.id, !ext.isEnabled)}
                >
                  {ext.isEnabled ? t('extensions.disable') : t('extensions.enable')}
                </button>
                <button
                  type="button"
                  className="export-inline-btn danger"
                  disabled={busy}
                  onClick={() => setConfirmId(ext.id)}
                >
                  {t('extensions.uninstall')}
                </button>
              </div>
            </section>
          ))}
        </div>
      )}

      {themes.length > 0 && (
        <div className="ext-settings-section">
          <h2 className="dashboard-title">{t('extensions.themesTitle')}</h2>
          <div className="ext-theme-list">
            {themes.map((theme) => {
              const active = activeTheme === theme.name
              return (
                <button
                  key={`${theme.extensionId}:${theme.name}`}
                  type="button"
                  className={`ext-theme-chip${active ? ' active' : ''}`}
                  onClick={() => {
                    applyExtensionTheme(active ? null : theme.name, active ? null : theme.accentColor)
                    setActiveTheme(active ? null : theme.name)
                  }}
                >
                  <span
                    className="ext-theme-swatch"
                    style={{ backgroundColor: theme.accentColor ?? 'transparent' }}
                    aria-hidden
                  />
                  {theme.name}
                </button>
              )
            })}
          </div>
        </div>
      )}

      <ExtensionSettings />
        </>
      )}

      {confirmTarget && (
        <ConfirmDialog
          title={t('extensions.uninstall')}
          message={t('extensions.uninstallConfirm', { name: confirmTarget.name })}
          onConfirm={() => void doUninstall(confirmTarget.id)}
          onCancel={() => setConfirmId(null)}
        />
      )}
    </div>
  )
}
