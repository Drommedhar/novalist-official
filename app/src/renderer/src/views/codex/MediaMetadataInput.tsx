import { useEffect, useRef, useState } from 'react'
import type { FieldBinding } from '../../collaboration/FocusedField'
import { SharedMetadataField } from '../../collaboration/SharedMetadataField'

export function MediaMetadataInput({ binding, shared, value, label, placeholder, className, save, prepare, trim }: {
  binding?: FieldBinding; shared: boolean; value: string; label: string; placeholder?: string
  className: string; save(value: string): void; prepare?(): Promise<void>; trim?: boolean
}): React.JSX.Element {
  const wantsFocus = useRef(false)
  const input = useRef<HTMLInputElement>(null)
  const pending = useRef(false)
  const [error, setError] = useState('')
  useEffect(() => { if (binding && wantsFocus.current) input.current?.focus() }, [binding?.itemId])
  const focus = async (): Promise<void> => {
    wantsFocus.current = true
    if (binding || !prepare || pending.current) return
    pending.current = true
    setError('')
    try { await prepare() }
    catch (failure) { setError(String(failure)) }
    finally { pending.current = false }
  }
  if (!shared) return <input className={className} aria-label={label} placeholder={placeholder}
    defaultValue={value} key={value} onBlur={event => save(event.target.value)} />
  return <div className="media-metadata-input" onFocusCapture={() => void focus()}
    onBlurCapture={event => { if (!event.currentTarget.contains(event.relatedTarget)) wantsFocus.current = false }}>
    <SharedMetadataField binding={binding} value={value}>
      {field => <input ref={input} className={className} aria-label={label} placeholder={placeholder}
        value={field.value ?? ''} readOnly={!field.editable} onChange={event => field.change(event.target.value)}
        onBlur={() => { if (trim && field.editable) field.change(field.value.trim()) }} />}
    </SharedMetadataField>
    {error && <p role="alert">{error}</p>}
  </div>
}
