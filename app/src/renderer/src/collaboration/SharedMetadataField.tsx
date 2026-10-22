import type { FieldBinding, SharedEntityFields } from './FocusedField'
import { useFocusedField } from './useFocusedField'
import { SharedFieldStatus } from './SharedFieldStatus'

export function metadataBinding(record: { sharedFields?: SharedEntityFields } | null | undefined, key: string): FieldBinding | undefined {
  const shared = record?.sharedFields
  const itemId = shared?.fields[key]
  return shared && itemId ? { sessionId: shared.sessionId, itemId } : undefined
}

interface MetadataControl<T> { value: T; editable: boolean; change(value: T | ((previous: T) => T)): void }
interface MetadataProps<T> {
  binding?: FieldBinding; value: T; onChange(value: T): void; children(control: MetadataControl<T>): React.ReactNode
}

export function SharedMetadataField<T>(props: MetadataProps<T>): React.JSX.Element {
  if (props.binding) return <BoundMetadataField {...props} binding={props.binding} />
  return <>{props.children({ value: props.value, editable: true,
    change: next => props.onChange(typeof next === 'function' ? (next as (previous: T) => T)(props.value) : next) })}</>
}

function BoundMetadataField<T>({ binding, value, children }: MetadataProps<T> & { binding: FieldBinding }): React.JSX.Element {
  const field = useFocusedField(binding, JSON.stringify(value))
  const { controller, view } = field
  const change = (next: T | ((previous: T) => T)): void => {
    const apply = (): void => {
      if (controller.snapshot.phase !== 'editing') return
      const previous = JSON.parse(controller.snapshot.value) as T
      controller.change(JSON.stringify(typeof next === 'function' ? (next as (previous: T) => T)(previous) : next))
    }
    if (view.phase === 'editing') apply()
    else void controller.focus().then(apply)
  }
  return <div onFocusCapture={() => void controller.focus()} onBlurCapture={event => {
    if (!event.currentTarget.contains(event.relatedTarget)) queueMicrotask(field.blur)
  }}>
    {children({ value: JSON.parse(view.value) as T, editable: view.phase === 'editing', change })}
    <SharedFieldStatus binding={binding} field={field} />
  </div>
}
