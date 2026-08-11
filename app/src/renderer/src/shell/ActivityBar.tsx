import { useLayoutEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  Languages,
  BookOpen,
  Blocks,
  CalendarDays,
  ChartNoAxesGantt,
  FileText,
  FolderGit2,
  Grid3x3,
  Images,
  LayoutDashboard,
  BookCopy,
  Library,
  MoreHorizontal,
  SpellCheck,
  SquareDashedMousePointer,
  Map,
  MessageSquareQuote,
  Network,
  Newspaper,
  ScrollText,
  Send,
  Settings
} from 'lucide-react'
import { activityGroups, useShellStore, type MainView } from '../stores/shellStore'
import { useExtensionsStore } from '../stores/extensionsStore'
import { ContextMenu, type ContextMenuItem } from './ContextMenu'

/**
 * Slim icon-only activity bar (the left-most 44px rail), mirroring the desktop
 * MainWindow activity bar. This is the top-level view switcher; the binder to
 * its right is only the chapter/scene tree. The editor ("write") is reached by
 * opening a scene, so it has no rail button.
 *
 * The rail holds more than a short window has room for - nineteen views before
 * a single extension contributes one - so what does not fit moves into a "..."
 * menu at the end of the list rather than running off the bottom of the screen.
 * Extensions and Settings are pinned below it and never overflow: they are how
 * you reach the extension that added the icon you are looking for, and how you
 * change anything at all.
 */

type IconComponent = React.ComponentType<{ size?: number; strokeWidth?: number }>

const viewIcons: Partial<Record<MainView, IconComponent>> = {
  manuscript: BookOpen,
  dashboard: LayoutDashboard,
  timeline: ChartNoAxesGantt,
  plotGrid: Grid3x3,
  calendar: CalendarDays,
  relationships: Network,
  dialogue: MessageSquareQuote,
  style: SpellCheck,
  canvas: SquareDashedMousePointer,
  series: BookCopy,
  codex: Library,
  wiki: Newspaper,
  maps: Map,
  languages: Languages,
  research: FileText,
  gallery: Images,
  expose: ScrollText,
  export: Send,
  git: FolderGit2
}

/** The icon an extension view falls back to when it contributes no path. */
const EXT_FALLBACK_ICON = 'M12 2 2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5'

// Views unavailable in the mobile sandbox, hidden from the activity bar there.
// Phase 3: Git/versioning (no `git` binary on iOS/Android). Phase 4 may extend this.
const mobileHiddenViews = new Set<MainView>(['git'])
const EMPTY_HIDDEN_VIEWS = new Set<MainView>()

/** One slot on the rail, in the order the slots are laid out. */
type RailEntry =
  | { kind: 'sep'; id: string }
  | { kind: 'view'; id: string; view: MainView }
  | { kind: 'ext'; id: string; extensionId: string; key: string; title: string; iconPath?: string }

/**
 * How much vertical room one rail element takes, its own margins included.
 *
 * Read off the element rather than restated here, so the sizes stay the design
 * tokens' business: a token change reflows the rail instead of silently making
 * this arithmetic wrong.
 */
function strideOf(el: HTMLElement): number {
  const style = getComputedStyle(el)
  return el.offsetHeight + parseFloat(style.marginTop) + parseFloat(style.marginBottom)
}

export function ActivityBar(): React.JSX.Element {
  const { t } = useTranslation()
  const hiddenViews = window.novalist.isMobile ? mobileHiddenViews : EMPTY_HIDDEN_VIEWS
  const mainView = useShellStore((s) => s.mainView)
  const extView = useShellStore((s) => s.extView)
  const setMainView = useShellStore((s) => s.setMainView)
  const setExtView = useShellStore((s) => s.setExtView)
  const extViews = useExtensionsStore((s) => s.views).filter((v) => v.placement === 'main')

  const entries: RailEntry[] = []
  for (const group of activityGroups) {
    const views = group.views.filter((view) => !hiddenViews.has(view) && viewIcons[view])
    if (views.length === 0) continue
    // Keyed off what has already been laid out rather than off the group's
    // index, so a group hidden on mobile does not leave a divider at the top.
    if (entries.length > 0) entries.push({ kind: 'sep', id: `sep-${group.key}` })
    for (const view of views) entries.push({ kind: 'view', id: view, view })
  }
  if (extViews.length > 0) {
    if (entries.length > 0) entries.push({ kind: 'sep', id: 'sep-ext' })
    for (const view of extViews) {
      entries.push({
        kind: 'ext',
        id: `${view.extensionId}|${view.key}`,
        extensionId: view.extensionId,
        key: view.key,
        title: view.title,
        iconPath: view.iconPath
      })
    }
  }

  const topRef = useRef<HTMLDivElement>(null)
  // Last known element sizes. Kept across measurements because the elements
  // they came from may themselves be in the overflow by the time we remeasure.
  const strides = useRef({ item: 0, sep: 0 })
  const [visibleCount, setVisibleCount] = useState(entries.length)
  const [menuAt, setMenuAt] = useState<{ x: number; y: number } | null>(null)

  const signature = entries.map((e) => e.id).join(' ')
  useLayoutEffect(() => {
    const top = topRef.current
    if (!top) return

    const measure = (): void => {
      const item = top.querySelector<HTMLElement>('.activity-bar-item')
      const sep = top.querySelector<HTMLElement>('.activity-bar-sep')
      if (item) strides.current.item = strideOf(item)
      if (sep) strides.current.sep = strideOf(sep)
      const { item: itemH, sep: sepH } = strides.current
      if (itemH <= 0) return

      // The rail's own box, which flex has already sized to whatever is left
      // over after the pinned buttons below it.
      const available = top.clientHeight
      const heightOf = (entry: RailEntry): number => (entry.kind === 'sep' ? sepH : itemH)

      let used = 0
      let count = 0
      while (count < entries.length && used + heightOf(entries[count]) <= available) {
        used += heightOf(entries[count])
        count++
      }
      // The "..." button has to fit in the same space it is reporting on.
      while (count > 0 && count < entries.length && used + itemH > available) {
        count--
        used -= heightOf(entries[count])
      }
      setVisibleCount(count)
    }

    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(top)
    return () => observer.disconnect()
    // `signature` stands in for the entry list: the same views in the same
    // order need no remeasure, and it is the identities that matter, not the
    // array instance rebuilt on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [signature])

  const shown = entries.slice(0, visibleCount)
  // A divider with nothing under it is just a line across the rail.
  while (shown.length > 0 && shown[shown.length - 1].kind === 'sep') shown.pop()
  const overflow = entries.slice(visibleCount).filter((e) => e.kind !== 'sep')

  const isActive = (entry: RailEntry): boolean =>
    entry.kind === 'view'
      ? !extView && mainView === entry.view
      : entry.kind === 'ext'
        ? extView?.key === entry.key && extView.extensionId === entry.extensionId
        : false

  const open = (entry: RailEntry): void => {
    if (entry.kind === 'view') setMainView(entry.view)
    else if (entry.kind === 'ext') setExtView({ extensionId: entry.extensionId, key: entry.key })
  }

  const labelOf = (entry: RailEntry): string =>
    entry.kind === 'ext' ? entry.title : entry.kind === 'view' ? t(`shell.view.${entry.view}`) : ''

  const renderEntry = (entry: RailEntry): React.JSX.Element => {
    if (entry.kind === 'sep') return <div key={entry.id} className="activity-bar-sep" />
    const active = isActive(entry)
    const label = labelOf(entry)
    const Icon = entry.kind === 'view' ? viewIcons[entry.view] : undefined
    return (
      <button
        key={entry.id}
        type="button"
        className={`activity-bar-item${active ? ' active' : ''}`}
        data-view={entry.kind === 'view' ? entry.view : undefined}
        data-tip={label}
        aria-label={label}
        aria-current={active ? 'page' : undefined}
        onClick={() => open(entry)}
      >
        {Icon ? (
          <Icon size={19} strokeWidth={1.75} />
        ) : (
          <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75">
            <path d={(entry.kind === 'ext' && entry.iconPath) || EXT_FALLBACK_ICON} />
          </svg>
        )}
      </button>
    )
  }

  const menuItems: ContextMenuItem[] = overflow.map((entry) => ({
    label: labelOf(entry),
    onClick: () => open(entry)
  }))
  // The view you are on can be one of the hidden ones; say so on the button
  // rather than leaving the rail looking as though nothing is selected.
  const overflowActive = overflow.some(isActive)

  return (
    <nav className="activity-bar" aria-label={t('shell.activityBar')}>
      <div className="activity-bar-top" ref={topRef}>
        {shown.map(renderEntry)}
        {overflow.length > 0 && (
          <button
            type="button"
            className={`activity-bar-item activity-bar-more${overflowActive ? ' active' : ''}`}
            data-tip={t('shell.activityBarMore')}
            aria-label={t('shell.activityBarMore')}
            aria-haspopup="menu"
            aria-expanded={menuAt !== null}
            onClick={(e) => {
              // Opens only. A second press lands outside the menu, which closes
              // it on the way down - toggling here would reopen it again.
              const box = e.currentTarget.getBoundingClientRect()
              setMenuAt({ x: box.right + 8, y: box.top })
            }}
          >
            <MoreHorizontal size={19} strokeWidth={1.75} />
          </button>
        )}
      </div>
      <div className="activity-bar-bottom">
        <button
          type="button"
          className={`activity-bar-item${!extView && mainView === 'extensions' ? ' active' : ''}`}
          data-tip={t('extensions.title')}
          aria-label={t('extensions.title')}
          aria-current={!extView && mainView === 'extensions' ? 'page' : undefined}
          onClick={() => setMainView('extensions')}
        >
          <Blocks size={19} strokeWidth={1.75} />
        </button>
        <button
          type="button"
          className={`activity-bar-item${!extView && mainView === 'settings' ? ' active' : ''}`}
          data-tip={t('shell.view.settings')}
          aria-label={t('shell.view.settings')}
          aria-current={!extView && mainView === 'settings' ? 'page' : undefined}
          onClick={() => setMainView('settings')}
        >
          <Settings size={19} strokeWidth={1.75} />
        </button>
      </div>
      {menuAt && menuItems.length > 0 && (
        <ContextMenu
          x={menuAt.x}
          y={menuAt.y}
          items={menuItems}
          onClose={() => setMenuAt(null)}
        />
      )}
    </nav>
  )
}
