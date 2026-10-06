import type { BinderState } from './binderState'
import type { ContextMenuItem } from './ContextMenu'

export function reorderItems(state: BinderState, index: number, orders: number[], move: (order: number) => void): ContextMenuItem[] {
  if (!state.isMobile) return []
  const items: ContextMenuItem[] = []
  if (index > 0) items.push({ label: state.t('explorer.moveUp'), onClick: () => move(orders[index - 1]) })
  if (index < orders.length - 1) items.push({ label: state.t('explorer.moveDown'), onClick: () => move(orders[index + 1]) })
  return items
}
