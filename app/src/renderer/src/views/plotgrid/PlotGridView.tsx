import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { useProjectStore } from '../../stores/projectStore'
import { ContextMenu } from '../../shell/ContextMenu'
import { InputDialog } from '../../shell/InputDialog'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { PromisesPanel } from './PromisesPanel'

interface PlotGridDto {
  plotlines: { id: string; name: string; color: string; order: number }[]
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
  const mainView = useShellStore((s) => s.mainView)
  const [grid, setGrid] = useState<PlotGridDto | null>(null)
  const [menu, setMenu] = useState<{ x: number; y: number; id: string; name: string } | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  // Which rows the grid is crossing the scenes with. Plotlines by default:
  // that is what a plot grid means before it means anything else.
  const [rowSource, setRowSource] = useState('plotline')

  useEffect(() => {
    if (mainView !== 'plotGrid') return
    void rpc.request<PlotGridDto>('plot/grid', [rowSource]).then(setGrid)
  }, [mainView, rowSource])

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
        {!byCodex && (
          <button
            className="toolbar-button toolbar-action"
            onClick={() => setPending({ kind: 'create' })}
          >
            <Plus size={14} strokeWidth={2} />
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
              label: t('explorer.contextRename'),
              onClick: () => setPending({ kind: 'rename', id: menu.id, current: menu.name })
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
