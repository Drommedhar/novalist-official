import { useTranslation } from 'react-i18next'
import { BookCopy, ChartNoAxesGantt, Globe, LayoutDashboard, PenLine, Send } from 'lucide-react'
import { HOME_VIEW, MODES, modeOf, type Mode } from './modes'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'

/**
 * The five workspaces, and the way home.
 *
 * What it replaces was nineteen destinations in a rail of unlabelled icons -
 * eight of them under one heading - that you told apart by hovering for a
 * tooltip, and that hid whatever did not fit behind a "...". Six entries fit in
 * any window, so nothing is ever hidden, and each one carries its name: the
 * complaint this restructure started from was being unable to find things, and
 * an icon you have to hover to identify is a thing you cannot find.
 */

type IconComponent = React.ComponentType<{ size?: number; strokeWidth?: number }>

const MODE_ICONS: Record<Mode, IconComponent> = {
  write: PenLine,
  plan: ChartNoAxesGantt,
  world: Globe,
  publish: Send,
  series: BookCopy
}

export function ModeRail(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const mode = useShellStore((s) => s.mode)
  const extView = useShellStore((s) => s.extView)
  const setMode = useShellStore((s) => s.setMode)
  const goHome = useShellStore((s) => s.goHome)
  // With no project open the modes have nothing to hold. They are shown
  // disabled rather than absent, so the shape of the app is the same before and
  // after opening one - which is the whole reason the start screen folded into
  // this window rather than replacing it.
  const isLoaded = useProjectStore((s) => s.isLoaded)
  const capacity = useShellStore((s) => s.shellCapacity)
  const docked = useShellStore((s) => s.modePanelDocked)
  const setModePanelOpen = useShellStore((s) => s.setModePanelOpen)

  /** Where the panel is not docked it is an overlay, so picking a mode raises it. */
  const pick = (entry: Mode): void => {
    setMode(entry)
    if (capacity === 'compact' || !docked) setModePanelOpen(true)
  }

  // Home wins whenever it is what is on screen; otherwise the rail follows the
  // mode, including while a view that belongs to none - Settings, About - is
  // showing, so the writer can see where they will land on the way back.
  const atHome = !extView && mainView === HOME_VIEW
  const activeMode = !atHome && modeOf(mainView) !== null ? modeOf(mainView) : null

  // Settings, Extensions and About open before a project does, and with no
  // project every button on this rail was disabled - so opening About from the
  // welcome screen was a room with the door locked behind you. Home is the way
  // out, and with nothing open it goes back to the welcome screen rather than
  // to a Dashboard there is no book for.
  const appScopedView =
    mainView === 'settings' || mainView === 'extensions' || mainView === 'about'
  const homeLabel = isLoaded ? t('shell.view.dashboard') : t('shell.welcome')

  return (
    <nav className="mode-rail" aria-label={t('shell.activityBar')}>
      <button
        type="button"
        className={`mode-rail-item${atHome ? ' active' : ''}`}
        aria-current={atHome ? 'page' : undefined}
        disabled={!isLoaded && !appScopedView}
        onClick={() => goHome()}
      >
        <LayoutDashboard size={19} strokeWidth={1.75} />
        {/* Titled as well as labelled: the rail is sized for the longest name
            any bundled language has, but a language somebody added themselves
            can be longer still, and then the tooltip is the way to read it. */}
        <span title={homeLabel}>{homeLabel}</span>
      </button>
      <div className="mode-rail-sep" />
      {MODES.map((entry) => {
        const Icon = MODE_ICONS[entry]
        // A mode is current when the writer is in one of its views. Picking a
        // mode and then opening Settings leaves it selected but not current,
        // which is the honest reading of where you are.
        const current = activeMode === entry
        const selected = !atHome && mode === entry
        return (
          <button
            key={entry}
            type="button"
            className={`mode-rail-item${current ? ' active' : ''}${
              selected && !current ? ' selected' : ''
            }`}
            data-mode={entry}
            aria-current={current ? 'page' : undefined}
            disabled={!isLoaded}
            onClick={() => pick(entry)}
          >
            <Icon size={19} strokeWidth={1.75} />
            <span title={t(`modes.${entry}`)}>{t(`modes.${entry}`)}</span>
          </button>
        )
      })}
    </nav>
  )
}
