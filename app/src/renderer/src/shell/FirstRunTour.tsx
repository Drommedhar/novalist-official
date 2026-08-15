import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore, type MainView, type MobileTab } from '../stores/shellStore'
import {
  hasSeenOnboardingTour,
  useOnboardingStore
} from '../stores/onboardingStore'

/**
 * One stop on the tour: what it is, and where to stand to see it.
 *
 * Every stop names a real view and switches to it. A tour that describes the
 * app without moving through it is a document, and there is already a manual
 * for that — what a first run needs is to have been in the rooms once.
 *
 * A phone reaches its views through the native tab bar rather than the pane
 * tree, so a stop names a tab there. Setting the view instead left the tour
 * describing rooms it never opened — the mobile shell drives the view from the
 * tab and put it straight back — and half of them (Plot Grid, Research, Export)
 * are not tabs at all, so it named places a phone cannot go.
 */
export type TourStepId =
  | 'dashboard'
  | 'manuscript'
  | 'editor'
  | 'focus-peek'
  | 'codex'
  | 'timeline'
  | 'research'
  | 'export'

export interface TourPrerequisites {
  /** A scene is loaded in an editor pane, so writing-only features can be tried. */
  hasOpenScene: boolean
  /** The host can point at, or otherwise demonstrate, an eligible entity mention. */
  focusPeekAvailable: boolean
}

export interface TourStep {
  id: TourStepId
  view: MainView
  tab?: MobileTab
  titleKey: string
  bodyKey: string
  taskKey: string
  requires?: (keyof TourPrerequisites)[]
}

const TOUR_STEPS: TourStep[] = [
  {
    id: 'dashboard',
    view: 'dashboard',
    tab: 'dashboard',
    titleKey: 'tour.dashboardTitle',
    bodyKey: 'tour.dashboardBody',
    taskKey: 'tour.dashboardTask'
  },
  {
    id: 'manuscript',
    view: 'manuscript',
    tab: 'manuscript',
    titleKey: 'tour.manuscriptTitle',
    bodyKey: 'tour.manuscriptBody',
    taskKey: 'tour.manuscriptTask'
  },
  {
    id: 'editor',
    view: 'write',
    tab: 'manuscript',
    titleKey: 'tour.editorTitle',
    bodyKey: 'tour.editorBody',
    taskKey: 'tour.editorTask',
    requires: ['hasOpenScene']
  },
  {
    id: 'focus-peek',
    view: 'write',
    tab: 'manuscript',
    titleKey: 'tour.focusPeekTitle',
    bodyKey: 'tour.focusPeekBody',
    taskKey: 'tour.focusPeekTask',
    requires: ['hasOpenScene', 'focusPeekAvailable']
  },
  {
    id: 'codex',
    view: 'codex',
    tab: 'codex',
    titleKey: 'tour.codexTitle',
    bodyKey: 'tour.codexBody',
    taskKey: 'tour.codexTask'
  },
  {
    id: 'timeline',
    view: 'timeline',
    tab: 'planning',
    titleKey: 'tour.timelineTitle',
    bodyKey: 'tour.timelineBody',
    taskKey: 'tour.timelineTask'
  },
  {
    id: 'research',
    view: 'research',
    titleKey: 'tour.researchTitle',
    bodyKey: 'tour.researchBody',
    taskKey: 'tour.researchTask'
  },
  {
    id: 'export',
    view: 'export',
    titleKey: 'tour.exportTitle',
    bodyKey: 'tour.exportBody',
    taskKey: 'tour.exportTask'
  }
]

/** The tasks this shell can actually reach. Phone-only builds expose five tabs. */
export function buildTourSteps(isMobile: boolean): TourStep[] {
  return isMobile ? TOUR_STEPS.filter((step) => step.tab !== undefined) : TOUR_STEPS
}

/**
 * Whether this machine has been shown the tour.
 *
 * Local storage rather than settings: it is a fact about this installation and
 * nothing to do with the book, so it must not travel with a project and must
 * not reappear because somebody opened a second one.
 */
export function hasSeenTour(): boolean {
  return hasSeenOnboardingTour()
}

export interface FirstRunTourProps {
  onClose(): void
  /** Override live project-derived prerequisites, useful to a host or a test. */
  prerequisites?: Partial<TourPrerequisites>
  /** Announces task changes so a host can highlight the relevant control. */
  onStepChange?(step: TourStep): void
  /** Point out an eligible mention when the Focus Peek task asks to try one. */
  onFocusPeekRequest?(): void
  /** Defaults to true: leave the writer in the workspace they started from. */
  restoreOnClose?: boolean
}

type TourShellSnapshot = Pick<
  ReturnType<typeof useShellStore.getState>,
  'mainView' | 'mobileTab' | 'panes' | 'activePaneId' | 'extView'
>

function unmetPrerequisite(
  step: TourStep,
  prerequisites: TourPrerequisites
): keyof TourPrerequisites | null {
  return step.requires?.find((key) => !prerequisites[key]) ?? null
}

/**
 * A short walk through the views, on the first run.
 *
 * Novalist has eighteen of them behind four activity-bar groups, and a writer
 * arriving at a blank Dashboard has no way to know that the Plot Grid or the
 * Codex exist. The manual is thorough and nobody reads a manual before they
 * have a reason to.
 */
export function FirstRunTour({
  onClose,
  prerequisites,
  onStepChange,
  onFocusPeekRequest,
  restoreOnClose = true
}: FirstRunTourProps): React.JSX.Element {
  const { t } = useTranslation()
  const isMobile = window.novalist.isMobile === true
  const liveHasOpenScene = useProjectStore((state) => state.openSceneId !== null)
  const completeTour = useOnboardingStore((state) => state.completeTour)
  const skipTour = useOnboardingStore((state) => state.skipTour)
  const stops = useMemo(() => buildTourSteps(isMobile), [isMobile])
  const [at, setAt] = useState(0)
  const stop = stops[at]
  const snapshotRef = useRef<TourShellSnapshot | null>(null)
  const restoredRef = useRef(false)
  const restoreTimerRef = useRef<number | null>(null)

  if (snapshotRef.current === null) {
    const shell = useShellStore.getState()
    snapshotRef.current = {
      mainView: shell.mainView,
      mobileTab: shell.mobileTab,
      panes: shell.panes,
      activePaneId: shell.activePaneId,
      extView: shell.extView
    }
  }

  const available: TourPrerequisites = {
    hasOpenScene: prerequisites?.hasOpenScene ?? liveHasOpenScene,
    focusPeekAvailable: prerequisites?.focusPeekAvailable ?? onFocusPeekRequest !== undefined
  }
  const unmet = unmetPrerequisite(stop, available)

  const restoreSnapshot = useCallback((): void => {
    if (!restoreOnClose || restoredRef.current || snapshotRef.current === null) return
    restoredRef.current = true
    useShellStore.setState(snapshotRef.current)
  }, [restoreOnClose])

  useEffect(() => {
    // React Strict Mode performs an effect cleanup/setup pair without unmounting.
    // Defer the fallback by one task so that setup can cancel that rehearsal;
    // a real parent-driven unmount still restores the workspace.
    if (restoreTimerRef.current !== null) window.clearTimeout(restoreTimerRef.current)
    return () => {
      restoreTimerRef.current = window.setTimeout(restoreSnapshot, 0)
    }
  }, [restoreSnapshot])

  // Each stop actually goes there, so the tour is a walk rather than a
  // description of one. A phone switches the tab, which is what its shell
  // renders from; setting the view alone would be undone on the next frame.
  useEffect(() => {
    onStepChange?.(stop)
    if (unmet) return
    if (stop.tab && isMobile) useShellStore.getState().setMobileTab(stop.tab)
    else useShellStore.getState().setMainView(stop.view)
  }, [isMobile, onStepChange, stop, unmet])

  const close = useCallback(
    (completed: boolean): void => {
      restoreSnapshot()
      if (completed) completeTour()
      else skipTour()
      onClose()
    },
    [completeTour, onClose, restoreSnapshot, skipTour]
  )

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      close(false)
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [close])

  const prerequisiteText =
    unmet === 'hasOpenScene'
      ? t('tour.needsScene')
      : unmet === 'focusPeekAvailable'
        ? t('tour.focusPeekUnavailable')
        : null

  return (
    <div
      className="tour-card"
      role="dialog"
      aria-modal="false"
      aria-label={t('tour.title')}
      aria-describedby="tour-body"
    >
      <button className="tour-close" onClick={() => close(false)} aria-label={t('tour.close')}>
        <X size={16} strokeWidth={1.75} />
      </button>
      <div className="tour-step">
        {t('tour.step', { at: at + 1, of: stops.length })}
      </div>
      <h2 className="dialog-title">{t(stop.titleKey)}</h2>
      <p id="tour-body" className="tour-body">
        {t(stop.bodyKey)}
      </p>
      <div className="tour-task">
        <span className="tour-task-label">{t('tour.tryLabel')}</span>
        <span>{prerequisiteText ?? t(stop.taskKey)}</span>
        {stop.id === 'focus-peek' && !unmet && onFocusPeekRequest && (
          <button className="btn-secondary tour-try" onClick={onFocusPeekRequest}>
            {t('tour.tryFocusPeek')}
          </button>
        )}
      </div>
      <div className="tour-actions">
        {/* Skip is as prominent as Next. Somebody who already knows the app
            should not have to click through seven panels to start writing. */}
        <button className="btn-secondary" onClick={() => close(false)}>
          {t('tour.skip')}
        </button>
        {at > 0 && (
          <button className="btn-secondary" onClick={() => setAt((value) => value - 1)}>
            {t('tour.back')}
          </button>
        )}
        <button
          className="btn-primary"
          onClick={() =>
            at === stops.length - 1 ? close(true) : setAt((value) => value + 1)
          }
        >
          {at === stops.length - 1 ? t('tour.done') : t('tour.next')}
        </button>
      </div>
    </div>
  )
}
