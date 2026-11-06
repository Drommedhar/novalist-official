import { Library } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { runCommand } from './commands'

export function MobileLibraryButton(): React.JSX.Element {
  const { t } = useTranslation()
  return (
    <button type="button" className="mobile-library-button" onClick={() => runCommand('app.closeProject')}>
      <Library size={18} strokeWidth={2} />
      <span>{t('desktopRefresh.library')}</span>
    </button>
  )
}
