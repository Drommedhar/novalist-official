import { type SettingsBodyContext } from './settingsBodyContext'
import { SettingInput } from './SettingsInputs'

export function updatesIntegrationsSettingsBody(context: SettingsBodyContext): React.ReactNode {
  const { view, update, t } = context
  return (
    <>
      {/* App self-update is disabled on the Mac App Store build (the store
              delivers updates), so hide the toggle there. */}
      {!window.novalist.isMas && (
        <>
          <label className="relationships-toggle">
            <input
              id="set-check-updates"
              type="checkbox"
              checked={Boolean(view.global.checkForUpdates)}
              onChange={(e) => void update('global', { checkForUpdates: e.target.checked })}
            />
            {t('update.checkForUpdates')}
          </label>
          <div className="settings-hint">{t('update.checkForUpdatesDesc')}</div>
        </>
      )}
      <label className="relationships-toggle">
        <input
          id="set-extension-updates"
          type="checkbox"
          checked={Boolean(view.global.checkForExtensionUpdates)}
          onChange={(e) =>
            void update('global', { checkForExtensionUpdates: e.target.checked })
          }
        />
        {t('settings.checkForExtensionUpdates')}
      </label>
      <div className="settings-hint">{t('settings.checkForExtensionUpdatesDesc')}</div>
      <label className="inspector-label" htmlFor="set-github-token">
        {t('settings.githubToken')}
      </label>
      <SettingInput
        id="set-github-token"
        type="password"
        value={String(view.global.gitHubToken ?? '')}
        onCommit={(v) => void update('global', { gitHubToken: v.trim() || null })}
      />
      <div className="settings-hint">{t('settings.githubTokenDesc')}</div>
    </>
  )
}
