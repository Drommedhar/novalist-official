import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useProjectStore } from '../../stores/projectStore'
import { ContextMenu } from '../../shell/ContextMenu'
import { InputDialog } from '../../shell/InputDialog'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { CustomFieldsPanel } from '../../shell/CustomFieldsPanel'
import { PromisesPanel } from './PromisesPanel'
import { PlotlineDetailScreen, type Plotline } from './PlotlineDetailScreen'
import { PlotLanes } from './PlotLanes'
import { MobileGroup, MobileNav, MobileRow, useMobileNav } from '../../shell/MobileNav'
import { useIsPhone } from '../../shell/useIsPhone'

/**
 * The plot grid as a phone can show it.
 *
 * A matrix needs two axes on screen at once. On a 393px phone the scene names
 * have to be turned on their side to fit, which cost a quarter of the height
 * and still only showed seven columns, and the row labels were clipped
 * mid-word - "Mira and Tom", "The inheritan". Read the grid it could not be.
 *
 * The question the grid answers is "which scenes carry this thread", so on a
 * phone it is asked one thread at a time: the threads are a list, and opening
 * one lists every scene with its membership as a checkbox. Same data, same
 * toggle, one axis at a time.
 */
/**
 * One thread's scenes, as the pushed page shows them.
 *
 * Built from the grid the view holds at the moment it renders, never from the
 * grid that was on screen when the thread was tapped - a toggle replaces the
 * whole grid, and a page built once kept every checkbox at the state it had
 * before the tap.
 */
function PhoneThreadPage({
  grid,
  plotlineId,
  toggle
}: {
  grid: PlotGridDto
  plotlineId: string
  toggle: (chapterGuid: string, sceneId: string, plotlineId: string) => Promise<void>
}): React.JSX.Element {
  // Grouped by chapter, because a scene list without its chapters is a list
  // of titles with no place in the book.
  const chapters: { guid: string; title: string; scenes: PlotGridDto['columns'] }[] = []
  for (const column of grid.columns) {
    const last = chapters[chapters.length - 1]
    if (last && last.guid === column.chapterGuid) last.scenes.push(column)
    else chapters.push({ guid: column.chapterGuid, title: column.chapterTitle, scenes: [column] })
  }
  return (
    <div className="plotgrid-phone-page">
      {chapters.map((chapter) => (
        <MobileGroup key={chapter.guid} header={chapter.title}>
          {chapter.scenes.map((scene) => (
            // A label rather than a row with a box in it: the whole row is the
            // target then, which is what iOS does and what a thumb needs - a
            // 24px box on its own is a miss waiting to happen, and pressing at
            // one raises the selection loupe instead of ticking anything.
            <label key={scene.sceneId} className="mobile-row plotgrid-phone-scene">
              <span className="mobile-row-label">{scene.sceneTitle}</span>
              <input
                type="checkbox"
                className="plotgrid-phone-check"
                checked={scene.plotlineIds.includes(plotlineId)}
                onChange={() => void toggle(scene.chapterGuid, scene.sceneId, plotlineId)}
              />
            </label>
          ))}
        </MobileGroup>
      ))}
    </div>
  )
}

function PhonePlotGrid({
  grid,
  rowSource
}: {
  grid: PlotGridDto
  rowSource: string
}): React.JSX.Element {
  const { t } = useTranslation()
  const nav = useMobileNav()

  return (
    <div className="plotgrid-phone">
      <MobileGroup header={t(`plotGrid.rows${rowSource}`)}>
        {grid.plotlines.map((line) => {
          const count = grid.columns.filter((c) => c.plotlineIds.includes(line.id)).length
          return (
            <MobileRow
              key={line.id}
              label={line.name}
              value={t('plotGrid.sceneCount', { count })}
              onClick={() => nav.push({ id: line.id, title: line.name })}
            />
          )
        })}
      </MobileGroup>
      {grid.plotlines.length === 0 && <p className="codex-empty">{t('plotGrid.empty')}</p>}
    </div>
  )
}

interface PlotGridDto {
  plotlines: Plotline[]
  columns: {
    chapterGuid: string
    chapterTitle: string
    sceneId: string
    sceneTitle: string
    plotlineIds: string[]
  }[]
}

type Pending =
  | { kind: 'create' }
  | { kind: 'fields'; id: string; name: string }
  | { kind: 'rename'; id: string; current: string }
  | { kind: 'delete'; id: string; name: string }
  | {
      kind: 'note'
      chapterGuid: string
      sceneId: string
      plotlineId: string
      sceneTitle: string
      current: string
    }

/** What the grid's rows can be. Plotlines first, because that is the default. */
const ROW_SOURCES = ['plotline', 'character', 'location', 'item', 'lore'] as const

export function PlotGridView(): React.JSX.Element {
  const { t } = useTranslation()
  const [grid, setGrid] = useState<PlotGridDto | null>(null)
  const [menu, setMenu] = useState<{ x: number; y: number; id: string; name: string } | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  // Which thread the grid has stepped aside for. Held as an id rather than the
  // object so a save or a restore is read back from the refreshed grid.
  const [editingId, setEditingId] = useState<string | null>(null)
  // Which rows the grid is crossing the scenes with. Plotlines by default:
  // that is what a plot grid means before it means anything else.
  const [rowSource, setRowSource] = useState('plotline')
  // A matrix says which scenes a thread touches. Lanes say where two threads
  // meet, which is the question a revision actually asks.
  const [asLanes, setAsLanes] = useState(false)
  const isPhone = useIsPhone()

  useEffect(() => {
    void rpc.request<PlotGridDto>('plot/grid', [rowSource]).then(setGrid)
  }, [rowSource])

  if (!grid) return <div className="main-placeholder">{t('shell.backendConnecting')}</div>

  const byCodex = rowSource !== 'plotline'

  const toggle = async (chapterGuid: string, sceneId: string, plotlineId: string): Promise<void> => {
    // A Codex row says who is in the scene, so ticking one writes the cast
    // rather than plotline membership.
    setGrid(
      byCodex
        ? await rpc.request<PlotGridDto>('plot/toggleCast', [
            chapterGuid,
            sceneId,
            plotlineId,
            rowSource
          ])
        : await rpc.request<PlotGridDto>('plot/toggle', [chapterGuid, sceneId, plotlineId])
    )
  }

  // On a phone the matrix is unreadable, so the same data is asked one thread
  // at a time. The lanes view is a second matrix and goes the same way.
  if (isPhone) {
    return (
      <MobileNav
        title={t('shell.view.plotGrid')}
        renderPage={(plotlineId) =>
          grid.plotlines.some((line) => line.id === plotlineId) ? (
            <PhoneThreadPage grid={grid} plotlineId={plotlineId} toggle={toggle} />
          ) : null
        }
      >
        <div className="plotgrid">
          <div className="plotgrid-toolbar">
            <select
              className="dialog-input plotgrid-rowsource"
              value={rowSource}
              aria-label={t('plotGrid.rowSource')}
              onChange={(e) => setRowSource(e.target.value)}
            >
              {ROW_SOURCES.map((source) => (
                <option key={source} value={source}>
                  {t(`plotGrid.rows${source}`)}
                </option>
              ))}
            </select>
            {!byCodex && (
              <button
                className="toolbar-button toolbar-action"
                onClick={() => setPending({ kind: 'create' })}
              >
                {t('plotGrid.addPlotline')}
              </button>
            )}
          </div>
          <PhonePlotGrid grid={grid} rowSource={rowSource} />
        </div>
        {pending?.kind === 'create' && (
          <InputDialog
            title={t('plotGrid.addPlotline')}
            onCancel={() => setPending(null)}
            onSubmit={(name) => {
              setPending(null)
              void rpc.request<PlotGridDto>('plot/createPlotline', [name]).then(setGrid)
            }}
          />
        )}
      </MobileNav>
    )
  }

  // A thread is edited on a screen of its own rather than in a card sized to
  // the viewport: the fields get the room the grid was using, and nothing is
  // one stray click away from being thrown out.
  const editing = editingId === null ? null : grid.plotlines.find((p) => p.id === editingId)
  if (editing) {
    return (
      <PlotlineDetailScreen
        key={editing.id}
        plotline={editing}
        onBack={() => setEditingId(null)}
        onSaved={(next) => setGrid(next as PlotGridDto)}
      />
    )
  }

  return (
    <div className="plotgrid">
      <div className="plotgrid-toolbar">
        <select
          className="dialog-input plotgrid-rowsource"
          value={rowSource}
          aria-label={t('plotGrid.rowSource')}
          onChange={(e) => setRowSource(e.target.value)}
        >
          {ROW_SOURCES.map((source) => (
            <option key={source} value={source}>
              {t(`plotGrid.rows${source}`)}
            </option>
          ))}
        </select>
        <button
          className={`dialog-button${asLanes ? ' primary' : ''}`}
          onClick={() => setAsLanes(!asLanes)}
        >
          {t(asLanes ? 'plotGrid.asGrid' : 'plotGrid.asLanes')}
        </button>
        {!byCodex && (
          <button
            className="toolbar-button toolbar-action"
            onClick={() => setPending({ kind: 'create' })}
          >
            {t('plotGrid.addPlotline')}
          </button>
        )}
      </div>
      {/* Threads and promises are the same kind of thinking, so the promises
          live here rather than in a view of their own. */}
      <details className="plotgrid-promises">
        <summary>{t('promises.title')}</summary>
        <PromisesPanel />
      </details>
      {byCodex && <div className="settings-hint">{t('plotGrid.codexHint')}</div>}
      {grid.plotlines.length === 0 ? (
        <p className="codex-empty">{t('plotGrid.emptyHint')}</p>
      ) : asLanes ? (
        <PlotLanes
          plotlines={grid.plotlines}
          columns={grid.columns}
          onOpenScene={(chapterGuid, sceneId) =>
            void useProjectStore.getState().openScene(chapterGuid, sceneId)
          }
        />
      ) : (
        <div className="plotgrid-scroll">
          <table className="plotgrid-table">
            <thead>
              <tr>
                <th className="plotgrid-corner" />
                {grid.columns.map((col) => (
                  <th key={col.sceneId} className="plotgrid-scene" title={`${col.chapterTitle} - ${col.sceneTitle}`}>
                    <button
                      type="button"
                      className="plotgrid-scene-link"
                      onClick={() =>
                        void useProjectStore.getState().openScene(col.chapterGuid, col.sceneId)
                      }
                    >
                      {col.sceneTitle}
                    </button>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {grid.plotlines.map((plotline) => (
                <tr key={plotline.id}>
                  <th
                    className="plotgrid-rowlabel"
                    onContextMenu={(e) => {
                      // Renaming and deleting belong to plotlines. A Codex row
                      // is an entry, and it is renamed where it lives.
                      if (byCodex) return
                      e.preventDefault()
                      setMenu({ x: e.clientX, y: e.clientY, id: plotline.id, name: plotline.name })
                    }}
                  >
                    <span className="plotgrid-color" style={{ background: plotline.color }} />
                    {plotline.name}
                    {/* A grid of equal rows says the spine and a running joke
                        are the same kind of thing. Both marks are readable
                        without opening anything. */}
                    {!byCodex && plotline.importance === 'Main' && (
                      <span className="plotgrid-importance">{t('plotGrid.importanceMain')}</span>
                    )}
                    {!byCodex && plotline.unresolvedSteps > 0 && (
                      <span
                        className="plotgrid-unresolved"
                        title={t('plotGrid.unresolvedCount', { count: plotline.unresolvedSteps })}
                      >
                        {t('plotGrid.unresolvedBadge', { count: plotline.unresolvedSteps })}
                      </span>
                    )}
                  </th>
                  {grid.columns.map((col) => {
                    const assigned = col.plotlineIds.includes(plotline.id)
                    return (
                      <td key={col.sceneId}>
                        <button
                          className={`plotgrid-cell${assigned ? ' assigned' : ''}`}
                          style={assigned ? { background: plotline.color } : undefined}
                          title={`${col.chapterTitle} - ${col.sceneTitle}`}
                          onClick={() => void toggle(col.chapterGuid, col.sceneId, plotline.id)}
                        />
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {menu && (
        <ContextMenu
          x={menu.x}
          y={menu.y}
          items={[
            {
              label: t('plotGrid.detail'),
              onClick: () => setEditingId(menu.id)
            },
            {
              label: t('explorer.contextRename'),
              onClick: () => setPending({ kind: 'rename', id: menu.id, current: menu.name })
            },
            {
              label: t('props.yourFields'),
              onClick: () => setPending({ kind: 'fields', id: menu.id, name: menu.name })
            },
            {
              label: t('explorer.contextDelete'),
              danger: true,
              onClick: () => setPending({ kind: 'delete', id: menu.id, name: menu.name })
            }
          ]}
          onClose={() => setMenu(null)}
        />
      )}
      {pending?.kind === 'note' && (
        <InputDialog
          title={t('plotGrid.cellNoteTitle', { scene: pending.sceneTitle })}
          placeholder={pending.current || t('plotGrid.cellNotePlaceholder')}
          onCancel={() => setPending(null)}
          onSubmit={(note) => {
            const p = pending
            setPending(null)
            void rpc
              .request<PlotGridDto>('plot/setCellNote', [
                p.chapterGuid,
                p.sceneId,
                p.plotlineId,
                note
              ])
              .then(setGrid)
          }}
        />
      )}
      {pending?.kind === 'fields' && (
        <div
          className="dialog-overlay"
          onPointerDown={(e) => e.target === e.currentTarget && setPending(null)}
        >
          <div className="dialog-card" role="dialog" aria-label={pending.name}>
            <span className="dialog-title">{pending.name}</span>
            <CustomFieldsPanel scope="Plotline" id={pending.id} />
            <div className="dialog-actions">
              <button className="dialog-button" onClick={() => setPending(null)}>
                {t('dialog.close')}
              </button>
            </div>
          </div>
        </div>
      )}
      {pending?.kind === 'create' && (
        <InputDialog
          title={t('plotGrid.addPlotline')}
          onCancel={() => setPending(null)}
          onSubmit={(name) => {
            setPending(null)
            void rpc.request<PlotGridDto>('plot/createPlotline', [name]).then(setGrid)
          }}
        />
      )}
      {pending?.kind === 'rename' && (
        <InputDialog
          title={t('explorer.contextRename')}
          placeholder={pending.current}
          onCancel={() => setPending(null)}
          onSubmit={(name) => {
            const id = pending.id
            setPending(null)
            void rpc.request<PlotGridDto>('plot/renamePlotline', [id, name]).then(setGrid)
          }}
        />
      )}
      {pending?.kind === 'delete' && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={pending.name}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            const id = pending.id
            setPending(null)
            void rpc.request<PlotGridDto>('plot/deletePlotline', [id]).then(setGrid)
          }}
        />
      )}
    </div>
  )
}
