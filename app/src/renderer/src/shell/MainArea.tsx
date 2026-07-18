import { useTranslation } from 'react-i18next'
import { useShellStore } from '../stores/shellStore'

export function MainArea(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)

  return (
    <main className="main-area">
      <div className="main-placeholder">
        <h1>{t(`shell.view.${mainView}`)}</h1>
        <p>{t('shell.viewPending')}</p>
      </div>
    </main>
  )
}
