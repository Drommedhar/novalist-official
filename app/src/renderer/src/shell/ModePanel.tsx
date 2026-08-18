import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  AudioLines,
  BookOpen,
  CalendarDays,
  ChartNoAxesGantt,
  FileText,
  FolderGit2,
  Grid3x3,
  Images,
  Languages,
  Layers,
  Library,
  Map,
  MessageSquareQuote,
  Network,
  Newspaper,
  PenLine,
  ScrollText,
  Send,
  SpellCheck,
  SquareDashedMousePointer,
  BookCopy
} from 'lucide-react'
import {
  DEFAULT_EXTENSION_MODE,
  EXTENSION_GROUP,
  FILTER_THRESHOLD,
  MODES,
  MODE_GROUPS,
  type Mode
} from './modes'
import { useExtensionsStore } from '../stores/extensionsStore'
import { useCodexStore } from '../stores/codexStore'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore, type MainView } from '../stores/shellStore'

/**
 * The views inside the mode the writer is in.
 *
 * One switcher, the same in every mode: having found how to move between
 * Timeline and Calendar you already know how to move between Codex and Maps.
 * It is a grouped, scrolling, labelled list rather than a segmented control,
 * because a mode's list is unbounded - an extension declares the mode it joins,
 * so World can hold twenty views on a machine with a dozen extensions.
 *
 * Its rules, from the mock this was approved from:
 *
 * - extensions occupy their own group, last, so a contributed view never
 *   reorders a core one;
 * - a filter appears past ten views and is an accelerator, never the way
 *   something becomes reachable - nothing is ever hidden behind a "...", which
 *   is what the activity bar did and why things could not be found;
 * - counts sit right-aligned in the computed-value face;
 * - the mode head and the filter stay pinned while the list scrolls;
 * - at compact width the panel becomes an overlay holding the same rows in the
 *   same order.
 */

type IconComponent = React.ComponentType<{ size?: number; strokeWidth?: number }>

const VIEW_ICONS: Partial<Record<MainView, IconComponent>> = {
  write: PenLine,
  manuscript: BookOpen,
  drafts: Layers,
  narration: AudioLines,
  timeline: ChartNoAxesGantt,
  plotGrid: Grid3x3,
  canvas: SquareDashedMousePointer,
  relationships: Network,
  calendar: CalendarDays,
  dialogue: MessageSquareQuote,
  codex: Library,
  wiki: Newspaper,
  maps: Map,
  research: FileText,
  gallery: Images,
  languages: Languages,
  expose: ScrollText,
  style: SpellCheck,
  export: Send,
  git: FolderGit2,
  series: BookCopy
}

/** The icon an extension view falls back to when it contributes no path. */
const EXT_FALLBACK_ICON = 'M12 2 2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5'

/** A row in the panel: one of Novalist's views, or one an extension added. */
interface Row {
  id: string
  label: string
  view?: MainView
  extension?: { extensionId: string; key: string }
  iconPath?: string
  /** How much is in it. Absent where there is no honest number to show. */
  tally?: number
}

interface Group {
  label: string
  rows: Row[]
}

export function ModePanel({ overlay }: { overlay: boolean }): React.JSX.Element {
  const { t } = useTranslation()
  const mode = useShellStore((s) => s.mode)
  const mainView = useShellStore((s) => s.mainView)
  const extView = useShellStore((s) => s.extView)
  const setMainView = useShellStore((s) => s.setMainView)
  const setExtView = useShellStore((s) => s.setExtView)
  const closeModePanel = useShellStore((s) => s.setModePanelOpen)
  const extViews = useExtensionsStore((s) => s.views)
  const codexCount = useCodexStore((s) => s.entities.length)
  const sceneCount = useProjectStore((s) =>
    s.chapters.reduce((total, chapter) => total + chapter.scenes.length, 0)
  )
  const draftCount = useProjectStore((s) => s.drafts.length)
  const [query, setQuery] = useState('')

  const tallies: Partial<Record<MainView, number>> = {
    manuscript: sceneCount,
    drafts: draftCount,
    codex: codexCount
  }

  const groups = useMemo<Group[]>(() => {
    const core = MODE_GROUPS[mode].map((group) => ({
      label: t(group.labelKey),
      rows: group.views.map<Row>((view) => ({
        id: view,
        label: t(`shell.view.${view}`),
        view,
        ...(tallies[view] !== undefined ? { tally: tallies[view] } : {})
      }))
    }))
    const contributed = extViews
      .filter((view) => view.placement === 'main')
      // A manifest can name any string; one that is not a mode is treated as
      // no answer rather than as a mode nobody can reach.
      .filter((view) => {
        const named = view.mode as Mode | undefined
        return (named && MODES.includes(named) ? named : DEFAULT_EXTENSION_MODE) === mode
      })
      .map<Row>((view) => ({
        id: `${view.extensionId}|${view.key}`,
        label: view.title,
        extension: { extensionId: view.extensionId, key: view.key },
        iconPath: view.iconPath
      }))
    return contributed.length > 0
      ? [...core, { label: t(EXTENSION_GROUP), rows: contributed }]
      : core
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, extViews, t, sceneCount, codexCount])

  const total = groups.reduce((count, group) => count + group.rows.length, 0)
  const filtering = query.trim().length > 0
  const lower = query.trim().toLowerCase()
  // While filtering the groups collapse: the labels describe an order the
  // filtered list no longer has.
  const shown: Group[] = filtering
    ? [
        {
          label: '',
          rows: groups.flatMap((group) =>
            group.rows.filter((row) => row.label.toLowerCase().includes(lower))
          )
        }
      ]
    : groups
  const labelled = shown.length > 1

  const open = (row: Row): void => {
    if (row.view) setMainView(row.view)
    else if (row.extension) setExtView(row.extension)
    if (overlay) closeModePanel(false)
  }

  const isCurrent = (row: Row): boolean =>
    row.extension
      ? extView?.extensionId === row.extension.extensionId && extView.key === row.extension.key
      : !extView && mainView === row.view

  return (
    <nav className={`mode-panel${overlay ? ' overlay' : ''}`} aria-label={t(`modes.${mode}`)}>
      <div className="mode-panel-head">
        <h2 title={t(`modes.${mode}`)}>{t(`modes.${mode}`)}</h2>
        <span className="mode-panel-count">{t('modes.viewCount', { count: total })}</span>
      </div>
      {total > FILTER_THRESHOLD && (
        <div className="mode-panel-filter">
          <input
            type="text"
            value={query}
            placeholder={t('modes.filter')}
            aria-label={t('modes.filter')}
            onChange={(event) => setQuery(event.target.value)}
          />
        </div>
      )}
      <ul className="mode-panel-list">
        {shown.every((group) => group.rows.length === 0) && (
          <li className="mode-panel-empty">{t('modes.noMatch')}</li>
        )}
        {shown.map((group) => (
          <li key={group.label || 'all'}>
            {labelled && group.label && (
              <div className="mode-panel-group">{group.label}</div>
            )}
            {group.rows.map((row) => {
              const Icon = row.view ? VIEW_ICONS[row.view] : undefined
              const current = isCurrent(row)
              return (
                <button
                  key={row.id}
                  type="button"
                  className="mode-panel-row"
                  data-view={row.view}
                  aria-current={current ? 'true' : undefined}
                  onClick={() => open(row)}
                >
                  {Icon ? (
                    <Icon size={15} strokeWidth={1.6} />
                  ) : (
                    <svg
                      width="15"
                      height="15"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="1.6"
                      aria-hidden="true"
                    >
                      <path d={row.iconPath || EXT_FALLBACK_ICON} />
                    </svg>
                  )}
                  <span className="mode-panel-name">{row.label}</span>
                  {row.tally !== undefined && (
                    <span className="mode-panel-tally">{row.tally}</span>
                  )}
                </button>
              )
            })}
          </li>
        ))}
      </ul>
    </nav>
  )
}
