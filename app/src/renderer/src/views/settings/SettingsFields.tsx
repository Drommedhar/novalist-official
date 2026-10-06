import {
  Children,
  Fragment,
  isValidElement,
  type ReactNode
} from 'react'



/** Pair existing labels and controls without replacing their state or handlers. */
export function SettingsFields({ children }: { children: ReactNode }): React.JSX.Element {
  const flatten = (nodes: ReactNode): ReactNode[] => {
    const result: ReactNode[] = []
    // Assign keys only after flattening. Independent toArray calls on nested
    // fragments reuse keys and can leave controls from the previous section.
    Children.forEach(nodes, (node) => {
      if (isValidElement<{ children?: ReactNode }>(node) && node.type === Fragment) {
        result.push(...flatten(node.props.children))
      } else if (node != null) result.push(node)
    })
    return result
  }
  const nodes = flatten(children)
  const rows: ReactNode[] = []
  for (let i = 0; i < nodes.length; i++) {
    const node = nodes[i]
    const control = nodes[i + 1]
    if (
      isValidElement<{ htmlFor?: string }>(node) &&
      node.type === 'label' &&
      node.props.htmlFor &&
      isValidElement(control) &&
      control.type !== 'label'
    ) {
      const fields: ReactNode[] = [control]
      i++
      const hint = nodes[i + 1]
      if (
        isValidElement<{ className?: string }>(hint) &&
        hint.type === 'p' &&
        hint.props.className === 'settings-hint'
      ) {
        fields.push(hint)
        i++
      }
      rows.push(
        <div className="settings-field-row" key={node.props.htmlFor}>
          {node}
          <div>{Children.toArray(fields)}</div>
        </div>
      )
    } else rows.push(node)
  }
  return <>{Children.toArray(rows)}</>
}
