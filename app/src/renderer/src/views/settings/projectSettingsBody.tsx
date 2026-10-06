import { type SettingsBodyContext } from './settingsBodyContext'

export function projectSettingsBody(context: SettingsBodyContext): React.ReactNode {
  const { projectName, t, closeProjectFromSettings } = context
  return (
    <>
      {projectName && <p className="settings-open-project">{projectName}</p>}
      <p className="settings-hint">{t('settings.closeProjectDesc')}</p>
      <button className="dialog-button" onClick={() => void closeProjectFromSettings()}>
        {t('command.closeProject')}
      </button>
    </>
  )
}
