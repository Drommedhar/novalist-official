import type { ComponentProps } from 'react'
import type { FieldBinding } from './FocusedField'
import { SharedMetadataField } from './SharedMetadataField'
import { ManuscriptPropertyField } from '../shell/ManuscriptPropertyField'

export function SharedPropertyField({ binding, ...props }: ComponentProps<typeof ManuscriptPropertyField> & { binding?: FieldBinding }): React.JSX.Element {
  return <SharedMetadataField<string | null> binding={binding} value={props.value} onChange={props.onCommit}>
    {field => <ManuscriptPropertyField {...props} value={field.value ?? ''} readOnly={!field.editable}
      commitOnChange={Boolean(binding)} onCommit={field.change} />}
  </SharedMetadataField>
}
