import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  ChevronDown,
  ChevronRight,
  Eye,
  EyeOff,
  Lock,
  Unlock,
  Plus,
  Trash2,
  Focus
} from 'lucide-react'
import {
  findNode,
  walkNodes,
  type DropPosition,
  type ElementKind,
  type MapDataT,
  type MapLayerNodeT
} from './mapModel'

interface LayerPanelProps {
  data: MapDataT | null
  selectedNodeId: string | null
  expanded: Record<string, boolean>
  isolated: { kind: string; id: string } | null
  onSelectNode(id: string): void
  onToggleExpand(id: string): void
  onAddLayer(): void
  onAddChild(id: string): void
  onDeleteNode(id: string): void
  onRename(id: string, name: string): void
  onToggleHidden(id: string): void
  onToggleLocked(id: string): void
  onMoveNode(dragId: string, targetId: string, pos: DropPosition): void
  onMoveToRoot(dragId: string): void
  onSetOpacity(id: string, opacity: number): void
  onSetNodeZoom(id: string, min: number, max: number): void
  onSetFloorMode(id: string, on: boolean): void
  onSetActiveFloor(id: string, memberId: string): void
  onSetElementZoom(kind: ElementKind, id: string, min: number, max: number): void
  onToggleIsolate(kind: ElementKind, id: string): void
}

interface FlatRow {
  node: MapLayerNodeT
  depth: number
}

function isExpanded(node: MapLayerNodeT, expanded: Record<string, boolean>): boolean {
  return expanded[node.id] ?? node.expanded ?? true
}

export function LayerPanel(props: LayerPanelProps): React.JSX.Element {
  const { t } = useTranslation()
  const { data, selectedNodeId, expanded } = props
  const [renamingId, setRenamingId] = useState<string | null>(null)
  const [draft, setDraft] = useState('')
  const [dragId, setDragId] = useState<string | null>(null)
  const [drop, setDrop] = useState<{ id: string; pos: DropPosition } | null>(null)

  const rows = useMemo<FlatRow[]>(() => {
    if (!data) return []
    const out: FlatRow[] = []
    const recurse = (nodes: MapLayerNodeT[], depth: number): void => {
      for (const node of nodes) {
        out.push({ node, depth })
        if (node.children?.length && isExpanded(node, expanded)) recurse(node.children, depth + 1)
      }
    }
    recurse(data.layers, 0)
    return out
  }, [data, expanded])

  const selectedNode = data && selectedNodeId ? findNode(data, selectedNodeId) : null

  const commitRename = (id: string): void => {
    const value = draft.trim()
    setRenamingId(null)
    if (value) props.onRename(id, value)
  }

  const rowDragOver = (e: React.DragEvent, node: MapLayerNodeT): void => {
    if (!dragId || dragId === node.id) return
    e.preventDefault()
    const rect = e.currentTarget.getBoundingClientRect()
    const rel = (e.clientY - rect.top) / rect.height
    const pos: DropPosition = rel < 0.3 ? 'before' : rel > 0.7 ? 'after' : 'inside'
    setDrop({ id: node.id, pos })
  }

  return (
    <div className="map-layerpanel">
      <div className="map-panel-head">
        <span className="map-panel-title">{t('map.layerPanelTitle')}</span>
        <button
          type="button"
          className="map-panel-add"
          title={t('map.layerAddTooltip')}
          disabled={!data}
          onClick={props.onAddLayer}
        >
          <Plus size={13} strokeWidth={2} />
          {t('map.layerAdd')}
        </button>
      </div>

      <div
        className="map-layer-tree"
        onDragOver={(e) => {
          if (dragId) {
            e.preventDefault()
            setDrop(null)
          }
        }}
        onDrop={() => {
          if (dragId && !drop) props.onMoveToRoot(dragId)
          setDragId(null)
          setDrop(null)
        }}
      >
        {rows.map(({ node, depth }) => {
          const hasChildren = (node.children?.length ?? 0) > 0
          const open = isExpanded(node, expanded)
          const dropHint =
            drop?.id === node.id ? ` drop-${drop.pos}` : ''
          return (
            <div
              key={node.id}
              className={`map-layer-row${node.id === selectedNodeId ? ' active' : ''}${dropHint}`}
              style={{ paddingLeft: `${depth * 14 + 4}px` }}
              draggable={renamingId !== node.id}
              onDragStart={(e) => {
                setDragId(node.id)
                e.dataTransfer.effectAllowed = 'move'
              }}
              onDragEnd={() => {
                setDragId(null)
                setDrop(null)
              }}
              onDragOver={(e) => rowDragOver(e, node)}
              onDrop={(e) => {
                e.stopPropagation()
                if (dragId && drop) props.onMoveNode(dragId, drop.id, drop.pos)
                setDragId(null)
                setDrop(null)
              }}
              onClick={() => props.onSelectNode(node.id)}
            >
              <button
                type="button"
                className="map-layer-twisty"
                style={{ visibility: hasChildren ? 'visible' : 'hidden' }}
                onClick={(e) => {
                  e.stopPropagation()
                  props.onToggleExpand(node.id)
                }}
              >
                {open ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
              </button>

              {renamingId === node.id ? (
                <input
                  className="map-layer-rename"
                  autoFocus
                  value={draft}
                  onChange={(e) => setDraft(e.target.value)}
                  onClick={(e) => e.stopPropagation()}
                  onBlur={() => commitRename(node.id)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') commitRename(node.id)
                    else if (e.key === 'Escape') setRenamingId(null)
                  }}
                />
              ) : (
                <span
                  className="map-layer-name"
                  onDoubleClick={(e) => {
                    e.stopPropagation()
                    setRenamingId(node.id)
                    setDraft(node.name)
                  }}
                >
                  {node.name || node.id}
                </span>
              )}

              <span className="map-layer-actions">
                <button
                  type="button"
                  title={t('map.layerToggleVisibility')}
                  onClick={(e) => {
                    e.stopPropagation()
                    props.onToggleHidden(node.id)
                  }}
                >
                  {node.hidden ? <EyeOff size={13} /> : <Eye size={13} />}
                </button>
                <button
                  type="button"
                  title={t('map.layerToggleLock')}
                  onClick={(e) => {
                    e.stopPropagation()
                    props.onToggleLocked(node.id)
                  }}
                >
                  {node.locked ? <Lock size={13} /> : <Unlock size={13} />}
                </button>
                <button
                  type="button"
                  title={t('map.layerAddChildTooltip')}
                  onClick={(e) => {
                    e.stopPropagation()
                    props.onAddChild(node.id)
                  }}
                >
                  <Plus size={13} />
                </button>
                <button
                  type="button"
                  title={t('map.layerDelete')}
                  onClick={(e) => {
                    e.stopPropagation()
                    props.onDeleteNode(node.id)
                  }}
                >
                  <Trash2 size={13} />
                </button>
              </span>
            </div>
          )
        })}
        {rows.length === 0 && <div className="map-layer-empty">{t('map.layerPanel')}</div>}
      </div>

      {selectedNode && (
        <NodeProperties
          data={data as MapDataT}
          node={selectedNode}
          isolated={props.isolated}
          onSetOpacity={props.onSetOpacity}
          onSetNodeZoom={props.onSetNodeZoom}
          onSetFloorMode={props.onSetFloorMode}
          onSetActiveFloor={props.onSetActiveFloor}
          onSetElementZoom={props.onSetElementZoom}
          onToggleIsolate={props.onToggleIsolate}
        />
      )}
    </div>
  )
}

interface NodePropsProps {
  data: MapDataT
  node: MapLayerNodeT
  isolated: { kind: string; id: string } | null
  onSetOpacity(id: string, opacity: number): void
  onSetNodeZoom(id: string, min: number, max: number): void
  onSetFloorMode(id: string, on: boolean): void
  onSetActiveFloor(id: string, memberId: string): void
  onSetElementZoom(kind: ElementKind, id: string, min: number, max: number): void
  onToggleIsolate(kind: ElementKind, id: string): void
}

interface ElementRow {
  kind: ElementKind
  id: string
  name: string
  min: number
  max: number
}

function fileName(path: string): string {
  const parts = path.split(/[\\/]/)
  return parts[parts.length - 1] || path
}

function NodeProperties(props: NodePropsProps): React.JSX.Element {
  const { t } = useTranslation()
  const { node, data, isolated } = props
  const hasChildren = (node.children?.length ?? 0) > 0

  const elementRows: ElementRow[] = useMemo(() => {
    const rows: ElementRow[] = []
    for (const img of node.images ?? [])
      rows.push({ kind: 'image', id: img.id, name: fileName(img.path), min: img.minZoom ?? 0, max: img.maxZoom ?? 0 })
    for (const sp of node.splines ?? [])
      rows.push({
        kind: 'spline',
        id: sp.id,
        name: sp.preset ? `${sp.kind ?? 'road'}: ${sp.preset}` : sp.kind ?? 'spline',
        min: sp.minZoom ?? 0,
        max: sp.maxZoom ?? 0
      })
    for (const sh of node.shapes ?? [])
      rows.push({ kind: 'shape', id: sh.id, name: sh.type ?? 'shape', min: sh.minZoom ?? 0, max: sh.maxZoom ?? 0 })
    for (const b of node.buildings ?? [])
      rows.push({ kind: 'building', id: b.id, name: b.type ?? 'building', min: b.minZoom ?? 0, max: b.maxZoom ?? 0 })
    for (const p of data.pins ?? [])
      if ((p.layerId ?? '') === node.id)
        rows.push({ kind: 'pin', id: p.id, name: p.label || '(pin)', min: p.minZoom ?? 0, max: p.maxZoom ?? 0 })
    for (const l of data.labels ?? [])
      if ((l.layerId ?? '') === node.id)
        rows.push({
          kind: 'label',
          id: l.id,
          name: (l.text || '(label)').replace(/\n/g, ' '),
          min: l.minZoom ?? 0,
          max: l.maxZoom ?? 0
        })
    return rows
  }, [node, data])

  const memberChoices: MapLayerNodeT[] = node.children ?? []

  return (
    <div className="map-properties">
      <div className="map-panel-title">{t('map.properties')}</div>

      <label className="map-prop-row">
        <span>{t('map.propOpacity')}</span>
        <input
          type="range"
          min={0}
          max={100}
          value={Math.round((node.opacity ?? 1) * 100)}
          onChange={(e) => props.onSetOpacity(node.id, Number(e.target.value) / 100)}
        />
        <span className="map-prop-value">{Math.round((node.opacity ?? 1) * 100)}%</span>
      </label>

      <div className="map-prop-row">
        <span>{t('map.layerZoomFrom')}</span>
        <input
          type="number"
          min={0}
          className="map-prop-num"
          value={node.minZoom ?? 0}
          onChange={(e) => props.onSetNodeZoom(node.id, Number(e.target.value), node.maxZoom ?? 0)}
        />
        <span>{t('map.layerZoomTo')}</span>
        <input
          type="number"
          min={0}
          className="map-prop-num"
          value={node.maxZoom ?? 0}
          onChange={(e) => props.onSetNodeZoom(node.id, node.minZoom ?? 0, Number(e.target.value))}
        />
      </div>
      <div className="map-prop-caption">{t('map.layerZoomRangeCaption')}</div>

      {hasChildren && (
        <>
          <label className="map-prop-check" title={t('map.floorModeTooltip')}>
            <input
              type="checkbox"
              checked={!!node.isConnectedSet}
              onChange={(e) => props.onSetFloorMode(node.id, e.target.checked)}
            />
            <span>{t('map.floorMode')}</span>
          </label>
          {node.isConnectedSet && (
            <label className="map-prop-row">
              <span>{t('map.activeFloor')}</span>
              <select
                className="map-prop-select"
                value={node.defaultMemberLayerId ?? ''}
                onChange={(e) => props.onSetActiveFloor(node.id, e.target.value)}
              >
                {memberChoices.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name || c.id}
                  </option>
                ))}
              </select>
            </label>
          )}
        </>
      )}

      {elementRows.length > 0 && (
        <div className="map-prop-elements">
          {elementRows.map((row) => {
            const iso = isolated?.kind === row.kind && isolated.id === row.id
            return (
              <div key={`${row.kind}:${row.id}`} className="map-element-row">
                <span className="map-element-name" title={row.name}>
                  {row.name}
                </span>
                <button
                  type="button"
                  className={`map-element-iso${iso ? ' active' : ''}`}
                  title={t('map.isolateTooltip')}
                  onClick={() => props.onToggleIsolate(row.kind, row.id)}
                >
                  <Focus size={12} />
                </button>
                <input
                  type="number"
                  min={0}
                  className="map-prop-num"
                  title={t('map.imageMinZoom')}
                  value={row.min}
                  onChange={(e) => props.onSetElementZoom(row.kind, row.id, Number(e.target.value), row.max)}
                />
                <input
                  type="number"
                  min={0}
                  className="map-prop-num"
                  title={t('map.imageMaxZoom')}
                  value={row.max}
                  onChange={(e) => props.onSetElementZoom(row.kind, row.id, row.min, Number(e.target.value))}
                />
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

/** Exported for the host to derive a default active-layer leaf id. */
export function firstLeafId(data: MapDataT): string | null {
  let leaf: string | null = null
  walkNodes(data.layers, (n) => {
    if (!leaf && !(n.children?.length)) leaf = n.id
  })
  return leaf
}
