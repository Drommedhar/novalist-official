import {
  useEffect,
  useRef,
  useState
} from 'react'




export function SettingInput({
  value,
  onCommit,
  type,
  placeholder,
  id,
  list
}: {
  value: string
  onCommit(next: string): void
  type?: string
  placeholder?: string
  id?: string
  list?: string
}): React.JSX.Element {
  const [draft, setDraft] = useState(value)
  const focused = useRef(false)
  useEffect(() => {
    if (!focused.current) setDraft(value)
  }, [value])
  return (
    <input
      id={id}
      list={list}
      className="dialog-input"
      type={type}
      placeholder={placeholder}
      value={draft}
      onFocus={() => (focused.current = true)}
      onChange={(e) => setDraft(e.target.value)}
      onBlur={() => {
        focused.current = false
        if (draft !== value) onCommit(draft)
      }}
    />
  )
}




export function SettingNumber({
  id,
  value,
  min,
  max,
  step,
  onCommit
}: {
  id: string
  value: number
  min: number
  max: number
  step?: number
  onCommit(next: number): void
}): React.JSX.Element {
  const ref = useRef<HTMLInputElement>(null)
  const focused = useRef(false)

  useEffect(() => {
    if (!focused.current && ref.current) ref.current.value = String(value)
  }, [value])

  const commit = (): void => {
    focused.current = false
    const raw = ref.current?.value ?? ''
    const parsed = Number(raw)
    const next =
      raw.trim() === '' || Number.isNaN(parsed) ? value : Math.min(max, Math.max(min, parsed))
    if (ref.current) ref.current.value = String(next)
    if (next !== value) onCommit(next)
  }

  return (
    <input
      ref={ref}
      id={id}
      className="dialog-input"
      type="number"
      min={min}
      max={max}
      step={step}
      defaultValue={String(value)}
      onFocus={() => (focused.current = true)}
      onBlur={commit}
      // A phone's number pad has no return key; the keyboard's Done button
      // blurs and commits. On a desktop keyboard Enter should not have to be a
      // click somewhere else.
      onKeyDown={(e) => {
        if (e.key === 'Enter') e.currentTarget.blur()
      }}
    />
  )
}
