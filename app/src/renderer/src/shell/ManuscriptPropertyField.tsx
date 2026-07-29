import { useTranslation } from 'react-i18next'
import type { ManuscriptProperty } from '../stores/manuscriptPropsStore'

/**
 * One typed manuscript-property input, shared by every surface that edits them
 * so a date field looks and behaves the same in the scene dock, the chapter
 * dialog and the outliner column.
 *
 * Commits on blur for the free-text kinds and immediately for the ones where a
 * change is a single deliberate gesture - a tick or a pick.
 */
export function ManuscriptPropertyField(props: {
  property: ManuscriptProperty
  value: string
  onCommit: (value: string | null) => void
  className?: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const { property, value, onCommit } = props
  const className = props.className ?? 'inspector-input'

  switch (property.type) {
    case 'Bool':
      return (
        <input
          type="checkbox"
          aria-label={property.label}
          checked={value === 'true'}
          onChange={(e) => onCommit(e.target.checked ? 'true' : null)}
        />
      )
    case 'Int':
      return (
        <input
          className={className}
          type="number"
          aria-label={property.label}
          defaultValue={value}
          key={value}
          onBlur={(e) => onCommit(e.target.value)}
        />
      )
    case 'Date':
      return (
        <input
          className={className}
          type="date"
          aria-label={property.label}
          value={value}
          onChange={(e) => onCommit(e.target.value)}
        />
      )
    case 'Enum':
      return (
        <select
          className={className}
          aria-label={property.label}
          value={value}
          onChange={(e) => onCommit(e.target.value)}
        >
          <option value="">{t('props.unset')}</option>
          {property.enumOptions.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
      )
    default:
      return (
        <input
          className={className}
          aria-label={property.label}
          defaultValue={value}
          key={value}
          onBlur={(e) => onCommit(e.target.value)}
        />
      )
  }
}
