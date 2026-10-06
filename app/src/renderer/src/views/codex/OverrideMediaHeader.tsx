import { useTranslation } from 'react-i18next'
import { RotateCcw } from 'lucide-react'

/** Header row for one of the per-scope media override editors: a label plus a
 * "reset to inherit" affordance shown only while the scope owns the list. */
export function OverrideMediaHeader({
  labelKey,
  overriding,
  onReset
}: {
  labelKey: string
  overriding: boolean
  onReset: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  return (
    <div className="overrides-media-head">
      <span className="inspector-label">{t(labelKey)}</span>
      {overriding ? (
        <button className="overrides-reset" onClick={onReset}>
          <RotateCcw size={11} strokeWidth={2} />
          {t('entityEditor.overrideResetToInherit')}
        </button>
      ) : (
        <span className="overrides-inherit-tag">{t('entityEditor.overrideInheriting')}</span>
      )}
    </div>
  )
}
