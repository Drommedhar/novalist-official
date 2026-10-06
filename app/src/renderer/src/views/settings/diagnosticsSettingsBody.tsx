import { rpc } from '../../rpc/client'
import { type SettingsBodyContext } from './settingsBodyContext'

export function diagnosticsSettingsBody(context: SettingsBodyContext): React.ReactNode {
  const { isMobile, t, view, update } = context
  return (
    <>
      {displayDiagnosticsSettings(context)}
      <label className="relationships-toggle">
        <input
          id="set-diagnostic-logging"
          type="checkbox"
          checked={Boolean(view.global.diagnosticLoggingEnabled)}
          onChange={(e) =>
            void update('global', { diagnosticLoggingEnabled: e.target.checked })
          }
        />
        {t('settings.diagnosticLogging')}
      </label>
      <div className="settings-hint">{t('settings.diagnosticLoggingDesc')}</div>
      <div className="settings-button-row">
        {/* Revealing / opening the log in a file manager has no iOS equivalent
                (revealPath is a no-op in the sandbox), so hide those on mobile and
                keep only Clear logs. A "share log" action is a later addition. */}
        {!isMobile && (
          <>
            <button
              className="dialog-button"
              onClick={() => {
                void (async () => {
                  const info = await rpc.request<{
                    directory: string
                    currentLog: string | null
                  }>('settings/logInfo')
                  await window.novalist.openExternal(info.directory)
                })()
              }}
            >
              {t('settings.openLogFolder')}
            </button>
            <button
              className="dialog-button"
              onClick={() => {
                void (async () => {
                  const info = await rpc.request<{
                    directory: string
                    currentLog: string | null
                  }>('settings/logInfo')
                  if (info.currentLog) await window.novalist.openExternal(info.currentLog)
                  else await window.novalist.openExternal(info.directory)
                })()
              }}
            >
              {t('settings.openCurrentLog')}
            </button>
          </>
        )}
        <button
          className="dialog-button"
          onClick={() => void rpc.request('settings/clearLogs')}
        >
          {t('settings.clearLogs')}
        </button>
      </div>
    </>
  )
}

function displayDiagnosticsSettings(context: SettingsBodyContext): React.ReactNode {
  const { isMobile, t, displayInfoBusy, refreshDisplayInfo, displayInfo } = context
  return <>{!isMobile && window.novalist.displayDiagnostics && (
    <div className="settings-display-diagnostics">
      <div className="settings-desc">{t('settings.displayInfoDesc')}</div>
      <button
        id="set-display-diagnostics"
        className="dialog-button"
        disabled={displayInfoBusy}
        onClick={() => void refreshDisplayInfo()}
      >
        {t(displayInfoBusy ? 'settings.displayInfoReading' : 'settings.displayInfoRefresh')}
      </button>
      {displayInfo && (
        <dl
          className="settings-display-grid"
          data-testid="display-diagnostics"
          data-zoom-factor={displayInfo.zoomFactor}
          data-scale-factor={displayInfo.scaleFactor}
        >
          <div>
            <dt>{t('settings.uiScale')}</dt>
            <dd>{Math.round(displayInfo.zoomFactor * 100)}%</dd>
          </div>
          <div>
            <dt>{t('settings.osScale')}</dt>
            <dd>{Math.round(displayInfo.scaleFactor * 100)}%</dd>
          </div>
          <div>
            <dt>{t('settings.windowSize')}</dt>
            <dd>
              {displayInfo.windowBounds.width} × {displayInfo.windowBounds.height}
            </dd>
          </div>
          <div>
            <dt>{t('settings.contentSize')}</dt>
            <dd>
              {displayInfo.contentBounds.width} × {displayInfo.contentBounds.height}
            </dd>
          </div>
          <div>
            <dt>{t('settings.workAreaSize')}</dt>
            <dd>
              {displayInfo.workArea.width} × {displayInfo.workArea.height}
            </dd>
          </div>
        </dl>
      )}
    </div>
  )}</>
}
