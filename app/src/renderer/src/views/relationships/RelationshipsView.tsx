import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { layoutGraph, NODE_SIZE, type GraphCharacter } from './layout'
import './relationships.css'

// Distinct per-family box colors (cycles when there are more families than
// entries), mirroring the Avalonia RelationshipsGraph palette.
const BOX_PALETTE = [
  '#89b4fa',
  '#a6e3a1',
  '#fab387',
  '#f5c2e7',
  '#94e2d5',
  '#f9e2af',
  '#f38ba8',
  '#cba6f7',
  '#74c7ec',
  '#b4befe'
]

export function RelationshipsView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [characters, setCharacters] = useState<GraphCharacter[]>([])
  const [search, setSearch] = useState('')
  const [filterGroup, setFilterGroup] = useState('')
  const [filterRole, setFilterRole] = useState('')
  const [hideWorldBible, setHideWorldBible] = useState(false)
  const [zoom, setZoom] = useState(1)
  const [pan, setPan] = useState({ x: 0, y: 0 })
  const dragRef = useRef<{ startX: number; startY: number; panX: number; panY: number } | null>(null)
  const viewportRef = useRef<HTMLDivElement>(null)
  const fitPendingRef = useRef(false)

  useEffect(() => {
    if (mainView !== 'relationships') return
    void rpc.request<GraphCharacter[]>('relationships/graph').then(setCharacters)
  }, [mainView])

  const availableGroups = useMemo(
    () =>
      [...new Set(characters.map((c) => c.group).filter((g) => g.trim().length > 0))].sort((a, b) =>
        a.localeCompare(b)
      ),
    [characters]
  )
  const availableRoles = useMemo(
    () =>
      [...new Set(characters.map((c) => c.role).filter((r) => r.trim().length > 0))].sort((a, b) =>
        a.localeCompare(b)
      ),
    [characters]
  )

  const hasActiveFilter =
    search.length > 0 || filterGroup.length > 0 || filterRole.length > 0 || hideWorldBible

  const clearFilters = (): void => {
    setSearch('')
    setFilterGroup('')
    setFilterRole('')
    setHideWorldBible(false)
  }

  const filtered = useMemo(
    () =>
      characters.filter(
        (c) =>
          (!hideWorldBible || !c.isWorldBible) &&
          (filterGroup.length === 0 || c.group.toLowerCase() === filterGroup.toLowerCase()) &&
          (filterRole.length === 0 || c.role.toLowerCase() === filterRole.toLowerCase()) &&
          (search.length === 0 ||
            c.displayName.toLowerCase().includes(search.toLowerCase()) ||
            c.name.toLowerCase().includes(search.toLowerCase()))
      ),
    [characters, search, filterGroup, filterRole, hideWorldBible]
  )

  const layout = useMemo(() => layoutGraph(filtered), [filtered])

  // Fit-and-centre the graph in the viewport whenever it is rebuilt.
  const fitToGraph = useCallback(() => {
    const vp = viewportRef.current
    if (!vp) return
    const vw = vp.clientWidth
    const vh = vp.clientHeight
    if (vw <= 0 || vh <= 0) return
    let minX = Number.POSITIVE_INFINITY
    let minY = Number.POSITIVE_INFINITY
    let maxX = Number.NEGATIVE_INFINITY
    let maxY = Number.NEGATIVE_INFINITY
    for (const n of layout.nodes) {
      minX = Math.min(minX, n.x)
      minY = Math.min(minY, n.y)
      maxX = Math.max(maxX, n.x + NODE_SIZE.width)
      maxY = Math.max(maxY, n.y + NODE_SIZE.height)
    }
    for (const b of layout.boxes) {
      minX = Math.min(minX, b.x)
      minY = Math.min(minY, b.y)
      maxX = Math.max(maxX, b.x + b.width)
      maxY = Math.max(maxY, b.y + b.height)
    }
    if (!Number.isFinite(minX)) return
    const gw = Math.max(1, maxX - minX)
    const gh = Math.max(1, maxY - minY)
    const scale = Math.min(1.5, Math.max(0.2, Math.min((vw - 80) / gw, (vh - 80) / gh)))
    const cx = (minX + maxX) / 2
    const cy = (minY + maxY) / 2
    setZoom(scale)
    setPan({ x: vw / 2 - cx * scale, y: vh / 2 - cy * scale })
    fitPendingRef.current = false
  }, [layout])

  useLayoutEffect(() => {
    fitPendingRef.current = true
    fitToGraph()
  }, [fitToGraph])

  // The viewport may still be measuring on first mount; refit once it has a size.
  useEffect(() => {
    const vp = viewportRef.current
    if (!vp) return
    const observer = new ResizeObserver(() => {
      if (fitPendingRef.current) fitToGraph()
    })
    observer.observe(vp)
    return () => observer.disconnect()
  }, [fitToGraph])

  return (
    <div className="relationships">
      <div className="relationships-header">
        <span className="relationships-title">{t('relationships.title')}</span>
        <span className="relationships-hint">{t('relationships.hint')}</span>
      </div>
      <div className="timeline-toolbar">
        <input
          className="dialog-input relationships-search"
          placeholder={t('relationships.search')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <select
          className="toolbar-select"
          value={filterGroup}
          onChange={(e) => setFilterGroup(e.target.value)}
        >
          <option value="">{t('relationships.filterGroup')}</option>
          {availableGroups.map((g) => (
            <option key={g} value={g}>
              {g}
            </option>
          ))}
        </select>
        <select
          className="toolbar-select"
          value={filterRole}
          onChange={(e) => setFilterRole(e.target.value)}
        >
          <option value="">{t('relationships.filterRole')}</option>
          {availableRoles.map((r) => (
            <option key={r} value={r}>
              {r}
            </option>
          ))}
        </select>
        <label className="relationships-toggle">
          <input
            type="checkbox"
            checked={hideWorldBible}
            onChange={(e) => setHideWorldBible(e.target.checked)}
          />
          {t('relationships.hideWorldBible')}
        </label>
        {hasActiveFilter && (
          <button className="relationships-clear" onClick={clearFilters}>
            {t('relationships.clearFilters')}
          </button>
        )}
        <div className="toolbar-spacer" />
        <span className="calendar-header-label">{Math.round(zoom * 100)}%</span>
      </div>
      <div
        ref={viewportRef}
        className="relationships-viewport"
        onWheel={(e) => {
          e.preventDefault()
          const vp = viewportRef.current
          if (!vp) return
          const rect = vp.getBoundingClientRect()
          const cursorX = e.clientX - rect.left
          const cursorY = e.clientY - rect.top
          const factor = e.deltaY < 0 ? 1.1 : 1 / 1.1
          const newZoom = Math.min(4, Math.max(0.2, zoom * factor))
          if (Math.abs(newZoom - zoom) < 1e-6) return
          const gx = (cursorX - pan.x) / zoom
          const gy = (cursorY - pan.y) / zoom
          setZoom(newZoom)
          setPan({ x: cursorX - gx * newZoom, y: cursorY - gy * newZoom })
        }}
        onPointerDown={(e) => {
          dragRef.current = { startX: e.clientX, startY: e.clientY, panX: pan.x, panY: pan.y }
        }}
        onPointerMove={(e) => {
          const drag = dragRef.current
          if (!drag) return
          setPan({ x: drag.panX + e.clientX - drag.startX, y: drag.panY + e.clientY - drag.startY })
        }}
        onPointerUp={() => {
          dragRef.current = null
        }}
      >
        {layout.nodes.length === 0 ? (
          <p className="codex-empty">{t('relationships.emptyHint')}</p>
        ) : (
          <svg
            className="relationships-canvas"
            width={layout.width * zoom}
            height={layout.height * zoom}
            viewBox={`0 0 ${layout.width} ${layout.height}`}
            style={{ transform: `translate(${pan.x}px, ${pan.y}px)` }}
          >
            {layout.boxes.map((box, i) => {
              const color = BOX_PALETTE[i % BOX_PALETTE.length]
              return (
                <g key={i}>
                  <rect
                    x={box.x}
                    y={box.y}
                    width={box.width}
                    height={box.height}
                    className="relationships-familybox"
                    style={{ stroke: color, fill: `${color}22` }}
                    rx={8}
                  />
                  {box.label && (
                    <text
                      x={box.x + 8}
                      y={box.y + 16}
                      className="relationships-familylabel"
                      style={{ fill: color }}
                    >
                      {t('relationships.familyPrefix', { name: box.label })}
                    </text>
                  )}
                </g>
              )
            })}
            {layout.edges.map((edge, i) => (
              <g key={i}>
                <line
                  x1={edge.x1}
                  y1={edge.y1}
                  x2={edge.x2}
                  y2={edge.y2}
                  className="relationships-edge"
                />
                {edge.label && (
                  <text x={edge.labelX} y={edge.labelY} className="relationships-edgelabel">
                    {edge.label}
                  </text>
                )}
              </g>
            ))}
            {layout.nodes.map((node) => (
              <g key={node.id}>
                <rect
                  x={node.x}
                  y={node.y}
                  width={NODE_SIZE.width}
                  height={NODE_SIZE.height}
                  className="relationships-node"
                  rx={6}
                />
                <text
                  x={node.x + NODE_SIZE.width / 2}
                  y={node.y + NODE_SIZE.height / 2 + 4}
                  className="relationships-nodelabel"
                >
                  {node.name}
                </text>
              </g>
            ))}
          </svg>
        )}
      </div>
    </div>
  )
}
