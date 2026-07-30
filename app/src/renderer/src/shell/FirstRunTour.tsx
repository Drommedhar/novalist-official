import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useShellStore, type MainView } from '../stores/shellStore'

/**
 * One stop on the tour: what it is, and where to stand to see it.
 *
 * Every stop names a real view and switches to it. A tour that describes the
 * app without moving through it is a document, and there is already a manual
 * for that — what a first run needs is to have been in the rooms once.
 */
interface Stop {
  view: MainView
  titleKey: string
  bodyKey: string
}

const STOPS: Stop[] = [
  { view: 'dashboard', titleKey: 'tour.dashboardTitle', bodyKey: 'tour.dashboardBody' },
  { view: 'manuscript', titleKey: 'tour.manuscriptTitle', bodyKey: 'tour.manuscriptBody' },
  { view: 'codex', titleKey: 'tour.codexTitle', bodyKey: 'tour.codexBody' },
  { view: 'timeline', titleKey: 'tour.timelineTitle', bodyKey: 'tour.timelineBody' },
  { view: 'plotGrid', titleKey: 'tour.plotGridTitle', bodyKey: 'tour.plotGridBody' },
  { view: 'research', titleKey: 'tour.researchTitle', bodyKey: 'tour.researchBody' },
  { view: 'export', titleKey: 'tour.exportTitle', bodyKey: 'tour.exportBody' }
]

/**
 * Whether this machine has been shown the tour.
 *
 * Local storage rather than settings: it is a fact about this installation and
 * nothing to do with the book, so it must not travel with a project and must
 * not reappear because somebody opened a second one.
 */
const SEEN_KEY = 'nl.tour.seen'

export function hasSeenTour(): boolean {
  try {
    return localStorage.getItem(SEEN_KEY) === '1'
  } catch {
    // Private mode: better to skip the tour than to show it every launch.
    return true
  }
}

function markSeen(): void {
  try {
    localStorage.setItem(SEEN_KEY, '1')
  } catch {
    // Nothing to do; the tour simply offers itself again next time.
  }
}

interface FirstRunTourProps {
  onClose(): void
}

/**
 * A short walk through the views, on the first run.
 *
 * Novalist has eighteen of them behind four activity-bar groups, and a writer
 * arriving at a blank Dashboard has no way to know that the Plot Grid or the
 * Codex exist. The manual is thorough and nobody reads a manual before they
 * have a reason to.
 */
export function FirstRunTour({ onClose }: FirstRunTourProps): React.JSX.Element {
  const { t } = useTranslation()
  const [at, setAt] = useState(0)
  const stop = STOPS[at]

  // Each stop actually goes there, so the tour is a walk rather than a
  // description of one.
  useEffect(() => {
    useShellStore.getState().setMainView(stop.view)
  }, [stop.view])

  const finish = (): void => {
    markSeen()
    onClose()
  }

  return (
    <div className="tour-card" role="dialog" aria-label={t('tour.title')}>
      <div className="tour-step">
        {t('tour.step', { at: at + 1, of: STOPS.length })}
      </div>
      <h2 className="dialog-title">{t(stop.titleKey)}</h2>
      <p className="tour-body">{t(stop.bodyKey)}</p>
      <div className="tour-actions">
        {/* Skip is as prominent as Next. Somebody who already knows the app
            should not have to click through seven panels to start writing. */}
        <button className="btn-secondary" onClick={finish}>
          {t('tour.skip')}
        </button>
        {at > 0 && (
          <button className="btn-secondary" onClick={() => setAt(at - 1)}>
            {t('tour.back')}
          </button>
        )}
        <button
          className="btn-primary"
          onClick={() => (at === STOPS.length - 1 ? finish() : setAt(at + 1))}
        >
          {at === STOPS.length - 1 ? t('tour.done') : t('tour.next')}
        </button>
      </div>
    </div>
  )
}
