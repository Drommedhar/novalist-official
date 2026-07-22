import { Clock, LayoutGrid, CalendarDays, Replace } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import type { MainView } from '../stores/shellStore'

export type PlanningTarget = MainView | 'findReplace'

const ITEMS: { key: PlanningTarget; labelKey: string; Icon: typeof Clock }[] = [
  { key: 'timeline', labelKey: 'shell.view.timeline', Icon: Clock },
  { key: 'plotGrid', labelKey: 'shell.view.plotGrid', Icon: LayoutGrid },
  { key: 'calendar', labelKey: 'shell.view.calendar', Icon: CalendarDays },
  // Find & Replace is scoped search, not a view; it opens the dialog.
  { key: 'findReplace', labelKey: 'findReplace.title', Icon: Replace }
]

/**
 * The Plan tab's picker: a small liquid-glass popover that pops out just above the
 * tab bar (near the Plan button) listing the planning modes plus Find & Replace.
 * Selecting a mode switches the Plan tab's content; Find & Replace opens the
 * dialog. A tap outside the menu closes it.
 */
export function MobilePlanningMenu({
  onSelect,
  onClose
}: {
  onSelect: (target: PlanningTarget) => void
  onClose: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  return (
    <div
      className="mobile-planning-overlay"
      onPointerDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <div className="mobile-planning-menu" role="menu">
        {ITEMS.map(({ key, labelKey, Icon }) => (
          <button
            key={key}
            type="button"
            role="menuitem"
            className="mobile-planning-item"
            onClick={() => onSelect(key)}
          >
            <Icon size={18} strokeWidth={1.75} />
            <span>{t(labelKey)}</span>
          </button>
        ))}
      </div>
    </div>
  )
}
