import type { FieldBinding } from '../../collaboration/FocusedField'
import { useCodexStore } from '../../stores/codexStore'
import { useFocusedField } from '../../collaboration/useFocusedField'
import { SharedFieldStatus } from '../../collaboration/SharedFieldStatus'

export function SharedFieldControl({ binding, value, label, type, list, options, disabled }: {
  binding: FieldBinding; value: string; label?: string; type?: string; list?: string; options?: string[]; disabled?: boolean
}): React.JSX.Element {
  const field = useFocusedField(binding, value, async () => {
    const codex = useCodexStore.getState()
    if (codex.selectedId) await codex.select(codex.selectedId)
  })
  const { controller, view, blur } = field
  const editable = view.phase === 'editing'
  return <>
    {options ? <select className="dialog-input codex-field-input" aria-label={label} aria-disabled={!editable} disabled={disabled}
      value={view.value || options[0]} onFocus={() => controller.focus()} onBlur={blur}
      onKeyDown={event => { if (!editable && event.key !== 'Tab') event.preventDefault() }}
      onChange={event => controller.change(event.target.value)}>
      {options.map(option => <option key={option} value={option}>{option}</option>)}
    </select> : <input className="outliner-input codex-field-input" type={type ?? 'text'} aria-label={label}
      list={list} value={view.value} readOnly={!editable} disabled={disabled} onFocus={() => controller.focus()}
      onChange={event => controller.change(event.target.value)} onBlur={blur} />}
    <SharedFieldStatus binding={binding} field={field} />
  </>
}
