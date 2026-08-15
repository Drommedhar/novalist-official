import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { useOnboardingStore } from '../stores/onboardingStore'
import { useShellStore, type MainView } from '../stores/shellStore'

/**
 * What a view is for, said once, the first time it is opened.
 *
 * The walkthrough visits seven views. Novalist has twenty-two, so most of them
 * a writer meets cold - and a screen met cold has to be worked out from its own
 * controls, which is exactly the complaint the interface restructure started
 * from. Each view now introduces itself the first time you arrive, in two
 * sentences and one thing worth trying, and then never again.
 *
 * A strip above the view rather than a modal over it: you should be able to
 * read what the screen is for while looking at the screen. It is the same
 * guidance system as the contextual tips, so **Settings**, **Accessibility**,
 * "Show contextual guidance while I learn" turns the whole thing off, and
 * nothing about what is in a book is recorded to decide when to show one.
 */

/**
 * Every view's introduction, exhaustive by type.
 *
 * A new `MainView` fails typecheck until somebody has written what it is for,
 * which is the point: the reason two dozen screens had no explanation is that
 * nothing ever required one.
 */
export const VIEW_INTROS: Record<MainView, { titleKey: string; bodyKey: string }> = {
  write: { titleKey: 'intro.write.title', bodyKey: 'intro.write.body' },
  manuscript: { titleKey: 'intro.manuscript.title', bodyKey: 'intro.manuscript.body' },
  dashboard: { titleKey: 'intro.dashboard.title', bodyKey: 'intro.dashboard.body' },
  timeline: { titleKey: 'intro.timeline.title', bodyKey: 'intro.timeline.body' },
  plotGrid: { titleKey: 'intro.plotGrid.title', bodyKey: 'intro.plotGrid.body' },
  calendar: { titleKey: 'intro.calendar.title', bodyKey: 'intro.calendar.body' },
  relationships: { titleKey: 'intro.relationships.title', bodyKey: 'intro.relationships.body' },
  dialogue: { titleKey: 'intro.dialogue.title', bodyKey: 'intro.dialogue.body' },
  canvas: { titleKey: 'intro.canvas.title', bodyKey: 'intro.canvas.body' },
  style: { titleKey: 'intro.style.title', bodyKey: 'intro.style.body' },
  codex: { titleKey: 'intro.codex.title', bodyKey: 'intro.codex.body' },
  wiki: { titleKey: 'intro.wiki.title', bodyKey: 'intro.wiki.body' },
  maps: { titleKey: 'intro.maps.title', bodyKey: 'intro.maps.body' },
  languages: { titleKey: 'intro.languages.title', bodyKey: 'intro.languages.body' },
  research: { titleKey: 'intro.research.title', bodyKey: 'intro.research.body' },
  gallery: { titleKey: 'intro.gallery.title', bodyKey: 'intro.gallery.body' },
  series: { titleKey: 'intro.series.title', bodyKey: 'intro.series.body' },
  expose: { titleKey: 'intro.expose.title', bodyKey: 'intro.expose.body' },
  export: { titleKey: 'intro.export.title', bodyKey: 'intro.export.body' },
  git: { titleKey: 'intro.git.title', bodyKey: 'intro.git.body' },
  extensions: { titleKey: 'intro.extensions.title', bodyKey: 'intro.extensions.body' },
  settings: { titleKey: 'intro.settings.title', bodyKey: 'intro.settings.body' },
  about: { titleKey: 'intro.about.title', bodyKey: 'intro.about.body' }
}

export function ViewIntro({ view }: { view: MainView }): React.JSX.Element | null {
  const { t } = useTranslation()
  // Subscribed rather than read once, so closing the card removes it and
  // turning the guidance off in Settings takes every one of them away at once.
  const due = useOnboardingStore((state) => state.shouldIntroduceView(view))
  const close = useOnboardingStore((state) => state.closeViewIntro)
  const tourOpen = useShellStore((state) => state.tourOpen)

  // The walkthrough is already explaining these views, one at a time. Two
  // explanations of the same screen at once is worse than neither.
  if (!due || tourOpen) return null

  const intro = VIEW_INTROS[view]

  return (
    <aside className="view-intro" data-view-intro={view}>
      <div className="view-intro-copy">
        <strong>{t(intro.titleKey)}</strong>
        <span>{t(intro.bodyKey)}</span>
      </div>
      <button
        type="button"
        className="view-intro-close"
        aria-label={t('intro.gotIt')}
        title={t('intro.gotIt')}
        onClick={() => close(view)}
      >
        <X size={15} strokeWidth={1.75} />
      </button>
    </aside>
  )
}
