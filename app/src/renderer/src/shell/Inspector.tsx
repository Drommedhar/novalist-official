import { useTranslation } from 'react-i18next'

export function Inspector(): React.JSX.Element {
  const { t } = useTranslation()
  return (
    <aside className="inspector">
      <div className="inspector-header">{t('shell.inspector')}</div>
      <div className="inspector-placeholder">{t('shell.inspectorEmpty')}</div>
    </aside>
  )
}
