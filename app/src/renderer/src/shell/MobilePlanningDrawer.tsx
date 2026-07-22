import { ChevronRight, Clock, LayoutGrid, CalendarDays, Replace } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { MobileSheet } from './MobileSheet'
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
 * The Plan tab's drawer: a bottom sheet listing the planning modes (Timeline,
 * Plot Grid, Calendar) plus Find & Replace. Selecting a mode switches the Plan
 * tab's content; Find & Replace opens the dialog. Reuses MobileSheet.
 */
export function MobilePlanningDrawer({
  onSelect,
  onClose
}: {
  onSelect: (target: PlanningTarget) => void
  onClose: () => void
}): React.JSX.Element {
  const { t } = useTranslation()
  return (
    <MobileSheet title={t('mobile.tab.planning')} onClose={onClose}>
      <div className="mobile-more-hub">
        {ITEMS.map(({ key, labelKey, Icon }) => (
          <button
            key={key}
            type="button"
            className="mobile-more-item"
            onClick={() => onSelect(key)}
          >
            <Icon size={20} strokeWidth={1.75} />
            <span className="mobile-more-label">{t(labelKey)}</span>
            <ChevronRight size={18} strokeWidth={2} className="mobile-more-chevron" />
          </button>
        ))}
      </div>
    </MobileSheet>
  )
}
