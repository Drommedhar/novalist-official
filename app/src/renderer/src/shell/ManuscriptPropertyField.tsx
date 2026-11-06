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
  readOnly?: boolean
  commitOnChange?: boolean
}): React.JSX.Element {
  const { t } = useTranslation()
  const { property, value, onCommit } = props
  const className = props.className ?? 'inspector-input'
  const textValue = props.commitOnChange ? { value } : { defaultValue: value }

  switch (property.type) {
    case 'Bool':
      return (
        <input
          type="checkbox"
          aria-label={property.label}
          checked={value === 'true'}
          aria-disabled={props.readOnly}
          onChange={(e) => onCommit(e.target.checked ? 'true' : null)}
        />
      )
    case 'Int':
      return (
        <input
          className={className}
          type="number"
          aria-label={property.label}
          {...textValue}
          key={props.commitOnChange ? undefined : value}
          readOnly={props.readOnly}
          onChange={event => { if (props.commitOnChange) onCommit(event.target.value) }}
          onBlur={event => { if (!props.commitOnChange && !props.readOnly) onCommit(event.target.value) }}
        />
      )
    case 'Date':
      return (
        <input
          className={className}
          type="date"
          aria-label={property.label}
          value={value}
          readOnly={props.readOnly}
          onChange={(e) => onCommit(e.target.value)}
        />
      )
    case 'Enum':
      return (
        <select
          className={className}
          aria-label={property.label}
          value={value}
          aria-disabled={props.readOnly}
          onKeyDown={event => { if (props.readOnly && event.key !== 'Tab') event.preventDefault() }}
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
          {...textValue}
          key={props.commitOnChange ? undefined : value}
          readOnly={props.readOnly}
          onChange={event => { if (props.commitOnChange) onCommit(event.target.value) }}
          onBlur={event => { if (!props.commitOnChange && !props.readOnly) onCommit(event.target.value) }}
        />
      )
  }
}
