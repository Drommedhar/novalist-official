import type { FieldBinding } from '../../collaboration/FocusedField'
import { SharedFieldStatus } from '../../collaboration/SharedFieldStatus'
import { useFocusedField } from '../../collaboration/useFocusedField'
import { useCodexStore } from '../../stores/codexStore'
import { EntityReferenceList } from './EntityReferenceList'

export function SharedEntityReferenceList({ binding, value, label, targetType, disabled }: {
  binding: FieldBinding; value: string; label: string; targetType: string; disabled?: boolean
}): React.JSX.Element {
  const field = useFocusedField(binding, value, async () => {
    const codex = useCodexStore.getState()
    if (codex.selectedId) await codex.select(codex.selectedId)
  })
  const { controller, view } = field
  return <div onFocusCapture={() => { if (!disabled) void controller.focus() }} onBlurCapture={event => {
    if (!event.currentTarget.contains(event.relatedTarget)) queueMicrotask(field.blur)
  }}>
    <EntityReferenceList targetType={targetType} label={label} value={view.value}
      editable={!disabled && view.phase === 'editing'} onChange={next => controller.change(next)} />
    <SharedFieldStatus binding={binding} field={field} />
  </div>
}
