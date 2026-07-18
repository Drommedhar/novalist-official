import { useTranslation } from 'react-i18next'
import { useShellStore } from '../stores/shellStore'

export function StatusBar(): React.JSX.Element {
  const { t } = useTranslation()
  const backendVersion = useShellStore((s) => s.backendVersion)

  return (
    <footer className="status-bar">
      <span />
      <span className="status-backend">
        {backendVersion
          ? t('shell.backendConnected', { version: backendVersion })
          : t('shell.backendConnecting')}
      </span>
    </footer>
  )
}
