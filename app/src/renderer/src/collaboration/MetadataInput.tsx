import { useEffect, useRef, useState, type InputHTMLAttributes } from 'react'

interface MetadataInputProps<T> extends Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'onFocus' | 'readOnly'> {
  value: T
  editable: boolean
  change(value: T): void
  format(value: T): string
  parse(value: string): T
}

export function MetadataInput<T>({ value, editable, change, format, parse, onBlur, ...props }: MetadataInputProps<T>): React.JSX.Element {
  const [draft, setDraft] = useState(() => format(value))
  const [focused, setFocused] = useState(false)
  const previousEditable = useRef(editable)
  useEffect(() => {
    if (!focused || !editable || !previousEditable.current) setDraft(format(value))
    previousEditable.current = editable
  }, [value, editable, focused, format])
  return <input {...props} value={focused && editable ? draft : format(value)} readOnly={!editable}
    onFocus={() => setFocused(true)} onBlur={event => { setFocused(false); onBlur?.(event) }}
    onChange={event => { setDraft(event.target.value); change(parse(event.target.value)) }} />
}
