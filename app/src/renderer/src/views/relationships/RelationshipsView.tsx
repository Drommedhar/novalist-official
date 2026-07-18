import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { layoutGraph, NODE_SIZE, type GraphCharacter } from './layout'

export function RelationshipsView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [characters, setCharacters] = useState<GraphCharacter[]>([])
  const [search, setSearch] = useState('')
  const [hideWorldBible, setHideWorldBible] = useState(false)
  const [zoom, setZoom] = useState(1)
  const [pan, setPan] = useState({ x: 0, y: 0 })
  const dragRef = useRef<{ startX: number; startY: number; panX: number; panY: number } | null>(null)

  useEffect(() => {
    if (mainView !== 'relationships') return
    void rpc.request<GraphCharacter[]>('relationships/graph').then(setCharacters)
  }, [mainView])

  const filtered = useMemo(
    () =>
      characters.filter(
        (c) =>
          (!hideWorldBible || !c.isWorldBible) &&
          (search.length === 0 ||
            c.displayName.toLowerCase().includes(search.toLowerCase()) ||
            c.name.toLowerCase().includes(search.toLowerCase()))
      ),
    [characters, search, hideWorldBible]
  )

  const layout = useMemo(() => layoutGraph(filtered), [filtered])

  return (
    <div className="relationships">
      <div className="timeline-toolbar">
        <input
          className="dialog-input relationships-search"
          placeholder={t('relationships.search')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <label className="relationships-toggle">
          <input
            type="checkbox"
            checked={hideWorldBible}
            onChange={(e) => setHideWorldBible(e.target.checked)}
          />
          {t('relationships.hideWorldBible')}
        </label>
        <div className="toolbar-spacer" />
        <span className="calendar-header-label">{Math.round(zoom * 100)}%</span>
      </div>
      <div
        className="relationships-viewport"
        onWheel={(e) => {
          e.preventDefault()
          setZoom((z) => Math.min(4, Math.max(0.2, z * (e.deltaY < 0 ? 1.1 : 0.9))))
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
            {layout.boxes.map((box, i) => (
              <g key={i}>
                <rect
                  x={box.x}
                  y={box.y}
                  width={box.width}
                  height={box.height}
                  className="relationships-familybox"
                  rx={8}
                />
                {box.label && (
                  <text x={box.x + 8} y={box.y + 16} className="relationships-familylabel">
                    {t('relationships.familyPrefix', { name: box.label })}
                  </text>
                )}
              </g>
            ))}
            {layout.edges.map((edge) => {
              const from = layout.nodes.find((n) => n.id === edge.from)
              const to = layout.nodes.find((n) => n.id === edge.to)
              if (!from || !to) return null
              const x1 = from.x + NODE_SIZE.width / 2
              const y1 = from.y + NODE_SIZE.height / 2
              const x2 = to.x + NODE_SIZE.width / 2
              const y2 = to.y + NODE_SIZE.height / 2
              return (
                <g key={`${edge.from}|${edge.to}`}>
                  <line
                    x1={x1}
                    y1={y1}
                    x2={x2}
                    y2={y2}
                    className={`relationships-edge${edge.family ? ' family' : ''}`}
                  />
                  {!edge.family && (
                    <text x={(x1 + x2) / 2} y={(y1 + y2) / 2 - 4} className="relationships-edgelabel">
                      {edge.label}
                    </text>
                  )}
                </g>
              )
            })}
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
