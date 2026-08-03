import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { useWikiStore } from '../../stores/wikiStore'
import { useProjectStore } from '../../stores/projectStore'
import { layoutGraph, parentMap, siblingMap, NODE_SIZE, type GraphCharacter } from './layout'
import { kinshipLabel, type KinshipRow } from './kinshipLabel'
import {
  layoutFamilyTree,
  TREE_NODE_WIDTH,
  TREE_NODE_HEIGHT
} from './familyTree'
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

/**
 * The longest label a node box can hold before the text runs out of it.
 *
 * A person's name fits; "Haus der Larsons" and "Halb geschriebenes Notizbuch"
 * do not, and once places and things share the canvas the overflow from two
 * neighbours meets in the middle and neither is readable. SVG text has no
 * ellipsis of its own, so the string is cut and the whole name stays in the
 * tooltip that was already there.
 */
const LABEL_LIMIT = 14

function fitLabel(name: string): string {
  const trimmed = name.trim()
  return trimmed.length <= LABEL_LIMIT ? trimmed : `${trimmed.slice(0, LABEL_LIMIT - 1)}…`
}

/**
 * How round each kind's box is. A silhouette is readable at a glance and at any
 * zoom, where a colour alone stops working once the graph is dense.
 */
const NODE_RADIUS: Record<string, number> = {
  character: 6,
  location: 0,
  item: 15,
  lore: 10,
  scene: 2
}

/** The entry kinds the graph can show. Characters first; it opens on them. */
const ENTRY_KINDS = ['character', 'location', 'item', 'lore', 'scene'] as const

export function RelationshipsView(): React.JSX.Element {
  const { t } = useTranslation()
  const [characters, setCharacters] = useState<GraphCharacter[]>([])
  const [allNodes, setAllNodes] = useState<GraphCharacter[]>([])
  const [search, setSearch] = useState('')
  const [filterGroup, setFilterGroup] = useState('')
  const [filterRole, setFilterRole] = useState('')
  const [hideWorldBible, setHideWorldBible] = useState(false)
  // Which kinds of entry are on the graph. Characters alone by default: that
  // is what this view has always been, and a first look at a full Codex with
  // everything on at once is unreadable.
  const [types, setTypes] = useState<string[]>(['character'])
  // Which entry the graph is centred on, and how far out it reaches. A whole
  // Codex on one canvas proves the links exist and answers nothing; the
  // question a writer has is "what is this one connected to".
  const [rootId, setRootId] = useState<string | null>(null)
  // A force layout answers "what is connected to what" and puts a grandmother
  // wherever there is room, so three generations read as a cloud. A tree puts a
  // generation on a line, which is the one thing a family view has to do.
  const [asTree, setAsTree] = useState(false)
  const [ancestorDepth, setAncestorDepth] = useState(3)
  const [descendantDepth, setDescendantDepth] = useState(3)
  const [treeHorizontal, setTreeHorizontal] = useState(false)
  /**
   * How each person is related to the one the view is centred on.
   *
   * The lines were always drawable and never readable: a writer could see that
   * two characters connect through three others and still not know that makes
   * one of them a great-aunt. Only asked for when a root is chosen, because
   * without one there is nothing to be related to.
   */
  const [kinship, setKinship] = useState<Record<string, string>>({})
  const [depth, setDepth] = useState(2)
  // Scenes as nodes. Novalist always knew which entities appear in which scene
  // and never drew that edge, so "where do these two meet" had no answer here.
  const [withScenes, setWithScenes] = useState(false)
  const [zoom, setZoom] = useState(1)
  const [pan, setPan] = useState({ x: 0, y: 0 })
  const dragRef = useRef<{ startX: number; startY: number; panX: number; panY: number } | null>(null)
  const movedRef = useRef(false)
  const viewportRef = useRef<HTMLDivElement>(null)
  const fitPendingRef = useRef(false)
  const typeOf = useRef(new Map<string, string>())
  // Scene nodes carry the chapter they are in, so one can be opened rather
  // than only looked at.
  const chapterOf = useRef(new Map<string, string>())
  const openEntity = useCallback((id: string): void => {
    // Ignore the click that ends a pan drag; only a genuine tap opens the article.
    if (movedRef.current) return
    const type = typeOf.current.get(id) ?? 'character'
    // A scene is not a Codex entry, so it opens in the editor rather than
    // sending the writer to an article that does not exist.
    if (type === 'scene') {
      const chapterGuid = chapterOf.current.get(id)
      if (chapterGuid) void useProjectStore.getState().openScene(chapterGuid, id)
      return
    }
    useShellStore.getState().setMainView('wiki')
    // A node knows what it is, so a location opens its own article rather than
    // a character article that does not exist.
    void useWikiStore.getState().openArticle(type, id)
  }, [])

  /**
   * Recentres on a node rather than leaving the view.
   *
   * Following a thread meant opening an article and coming back, which loses
   * the shape you were reading. Now the graph moves with you.
   */
  const recentre = useCallback((id: string): void => {
    if (movedRef.current) return
    setRootId(id)
  }, [])

  useEffect(() => {
    if (!rootId) {
      setKinship({})
      return
    }
    // Recentring quickly puts two of these in flight too, and the answers are
    // about different people.
    let current = true
    void rpc
      .request<KinshipRow[]>('relationships/kinship', [parentMap(allNodes), rootId])
      .then((rows) => {
        if (!current) return
        const next: Record<string, string> = {}
        for (const row of rows) next[row.entityId] = kinshipLabel(t, row)
        setKinship(next)
      })
      .catch(() => current && setKinship({}))
    return () => {
      current = false
    }
  }, [rootId, allNodes, t])

  useEffect(() => {
    // Centring on somebody and then widening the reach puts two fetches in
    // flight. Without this the older one can land last and win, so the graph
    // snaps back to the narrower view it was already leaving.
    let current = true
    void rpc
      .request<GraphCharacter[]>('relationships/graph', [rootId, depth, withScenes])
      .then((all) => {
        if (!current) return
        typeOf.current = new Map(all.map((n) => [n.id, n.entityType]))
        chapterOf.current = new Map(
          all.filter((n) => n.chapterGuid).map((n) => [n.id, n.chapterGuid!])
        )
        setCharacters(all)
      })
    return () => {
      current = false
    }
  }, [rootId, depth, withScenes])

  // Every entry, only to fill the "centre on" picker: the graph itself may be
  // showing a neighbourhood, and you have to be able to jump out of it.
  useEffect(() => {
    void rpc
      .request<GraphCharacter[]>('relationships/graph', [null, 4, false])
      .then(setAllNodes)
      .catch(() => setAllNodes([]))
  }, [])

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
          types.includes(c.entityType) &&
          (!hideWorldBible || !c.isWorldBible) &&
          (filterGroup.length === 0 || c.group.toLowerCase() === filterGroup.toLowerCase()) &&
          (filterRole.length === 0 || c.role.toLowerCase() === filterRole.toLowerCase()) &&
          (search.length === 0 ||
            c.displayName.toLowerCase().includes(search.toLowerCase()) ||
            c.name.toLowerCase().includes(search.toLowerCase()))
      ),
    [characters, search, filterGroup, filterRole, hideWorldBible, types]
  )

  const layout = useMemo(() => layoutGraph(filtered), [filtered])
  // Built from every node rather than the filtered set: a tree with a
  // generation missing is not a shorter tree, it is a wrong one.
  const tree = useMemo(
    () =>
      asTree && rootId
        ? layoutFamilyTree(
            allNodes,
            parentMap(allNodes),
            rootId,
            {
              ancestors: ancestorDepth,
              descendants: descendantDepth,
              horizontal: treeHorizontal
            },
            siblingMap(allNodes)
          )
        : null,
    [asTree, rootId, allNodes, ancestorDepth, descendantDepth, treeHorizontal]
  )

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
        <span className="relationships-kinds">
          {t('relationships.entryKinds')}
          {ENTRY_KINDS.map((kind) => (
            <label key={kind} className="relationships-toggle">
              <input
                type="checkbox"
                checked={types.includes(kind)}
                onChange={(e) =>
                  setTypes(
                    e.target.checked ? [...types, kind] : types.filter((k) => k !== kind)
                  )
                }
              />
              {t(`relationships.kind${kind}`)}
            </label>
          ))}
        </span>
        <label className="relationships-toggle">
          <input
            type="checkbox"
            checked={hideWorldBible}
            onChange={(e) => setHideWorldBible(e.target.checked)}
          />
          {t('relationships.hideWorldBible')}
        </label>

        {/* Centring on one entry turns a hairball into an answer. Two hops is
            usually where a family or a faction becomes a visible shape. */}
        <select
          className="dialog-input relationships-filter relationships-root"
          aria-label={t('relationships.centreOn')}
          value={rootId ?? ''}
          onChange={(e) => setRootId(e.target.value || null)}
        >
          <option value="">{t('relationships.wholeWorld')}</option>
          {[...allNodes]
            .filter((n) => n.entityType !== 'scene')
            .sort((a, b) => a.displayName.localeCompare(b.displayName))
            .map((n) => (
              <option key={n.id} value={n.id}>
                {n.displayName}
              </option>
            ))}
        </select>
        {/* Only for the graph: the tree is drawn from every entry and reaches
            as far as its own two generation controls say, so leaving this one
            up in tree view offered a third depth dropdown that did nothing. */}
        {rootId && !asTree && (
          <label className="relationships-field">
            <span>{t('relationships.depth')}</span>
            <select
              className="dialog-input relationships-filter relationships-depth"
              value={String(depth)}
              onChange={(e) => setDepth(Number(e.target.value))}
            >
              {[1, 2, 3, 4].map((d) => (
                <option key={d} value={d}>
                  {t('relationships.hops', { count: d })}
                </option>
              ))}
            </select>
          </label>
        )}
        {/* Generations rather than a force layout. Needs a root: a tree with
            no root is a forest, and a forest is what the canvas already is. */}
        <button
          className={`dialog-button${asTree ? ' primary' : ''}`}
          disabled={!rootId}
          title={rootId ? undefined : t('relationships.treeNeedsRoot')}
          onClick={() => setAsTree(!asTree)}
        >
          {t(asTree ? 'relationships.asGraph' : 'relationships.asTree')}
        </button>
        {asTree && rootId && (
          <>
            {/* A writer tracing a line of succession wants ten generations down
                and one up; the same view with both at ten is unreadable. The
                label is drawn rather than only announced: "3 up" beside "3 down"
                beside "2 steps" said nothing about which was which. */}
            <label className="relationships-field">
              <span>{t('relationships.ancestors')}</span>
              <select
                className="dialog-input relationships-filter relationships-depth"
                value={String(ancestorDepth)}
                onChange={(e) => setAncestorDepth(Number(e.target.value))}
              >
                {[0, 1, 2, 3, 5, 10].map((d) => (
                  <option key={d} value={d}>
                    {d}
                  </option>
                ))}
              </select>
            </label>
            <label className="relationships-field">
              <span>{t('relationships.descendants')}</span>
              <select
                className="dialog-input relationships-filter relationships-depth"
                value={String(descendantDepth)}
                onChange={(e) => setDescendantDepth(Number(e.target.value))}
              >
                {[0, 1, 2, 3, 5, 10].map((d) => (
                  <option key={d} value={d}>
                    {d}
                  </option>
                ))}
              </select>
            </label>
            <button
              className="dialog-button"
              onClick={() => setTreeHorizontal(!treeHorizontal)}
            >
              {t(treeHorizontal ? 'relationships.treeVertical' : 'relationships.treeHorizontal')}
            </button>
          </>
        )}
        {/* The edge that was always known and never drawn: which scenes these
            people are actually in together. */}
        <label className="relationships-toggle">
          <input
            type="checkbox"
            checked={withScenes}
            onChange={(e) => {
              setWithScenes(e.target.checked)
              // A scene node is useless with its class filtered out.
              if (e.target.checked && !types.includes('scene')) setTypes([...types, 'scene'])
            }}
          />
          {t('relationships.withScenes')}
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
          movedRef.current = false
        }}
        onPointerMove={(e) => {
          const drag = dragRef.current
          if (!drag) return
          if (Math.abs(e.clientX - drag.startX) + Math.abs(e.clientY - drag.startY) > 4)
            movedRef.current = true
          setPan({ x: drag.panX + e.clientX - drag.startX, y: drag.panY + e.clientY - drag.startY })
        }}
        onPointerUp={() => {
          dragRef.current = null
        }}
      >
        {tree ? (
          tree.nodes.length === 0 ? (
            <p className="codex-empty">{t('relationships.treeEmpty')}</p>
          ) : (
            <svg
              className="relationships-canvas"
              width={tree.width * zoom}
              height={tree.height * zoom}
              viewBox={`0 0 ${tree.width} ${tree.height}`}
              style={{ transform: `translate(${pan.x}px, ${pan.y}px)` }}
            >
              {/* Parent to child, drawn under the boxes so a line never crosses
                  a name. */}
              {tree.edges.map((edge, i) => {
                const from = tree.nodes.find((n) => n.id === edge.parentId)
                const to = tree.nodes.find((n) => n.id === edge.childId)
                if (!from || !to) return null
                return (
                  <line
                    key={`${edge.parentId}-${edge.childId}-${i}`}
                    className="tree-edge"
                    x1={from.x + TREE_NODE_WIDTH / 2}
                    y1={from.y + TREE_NODE_HEIGHT / 2}
                    x2={to.x + TREE_NODE_WIDTH / 2}
                    y2={to.y + TREE_NODE_HEIGHT / 2}
                  />
                )
              })}
              {tree.nodes.map((node) => (
                <g
                  key={node.id}
                  className="tree-node"
                  onClick={(e) => (e.altKey ? openEntity(node.id) : recentre(node.id))}
                >
                  <rect
                    x={node.x}
                    y={node.y}
                    width={TREE_NODE_WIDTH}
                    height={TREE_NODE_HEIGHT}
                    rx={6}
                    className={`tree-box${node.generation === 0 ? ' root' : ''}`}
                  />
                  <text
                    x={node.x + TREE_NODE_WIDTH / 2}
                    y={node.y + TREE_NODE_HEIGHT / 2 + 4}
                    className="tree-name"
                  >
                    {fitLabel(node.name)}
                  </text>
                  {/* The whole name, and what they are to the root - the same
                      answer the graph gives, kept when the shape changes. */}
                  <title>
                    {kinship[node.id] ? `${node.name} - ${kinship[node.id]}` : node.name}
                  </title>
                </g>
              ))}
            </svg>
          )
        ) : layout.nodes.length === 0 ? (
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
              const isRole = box.kind === 'role'
              return (
                <g key={i}>
                  <rect
                    x={box.x}
                    y={box.y}
                    width={box.width}
                    height={box.height}
                    className="relationships-familybox"
                    style={{
                      stroke: color,
                      fill: `${color}22`,
                      ...(isRole ? { strokeDasharray: '6 4' } : {})
                    }}
                    rx={8}
                  />
                  {(isRole || box.label) && (
                    <text
                      x={box.x + 8}
                      y={box.y + 16}
                      className="relationships-familylabel"
                      style={{ fill: color }}
                    >
                      {isRole ? box.label : t('relationships.familyPrefix', { name: box.label })}
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
                  data-category={edge.category || undefined}
                />
                {edge.label && (
                  <text x={edge.labelX} y={edge.labelY} className="relationships-edgelabel">
                    {edge.label}
                  </text>
                )}
              </g>
            ))}
            {layout.nodes.map((node) => (
              <g
                key={node.id}
                className="relationships-node-group"
                role="button"
                tabIndex={0}
                // Click follows the thread without leaving the view; the
                // article is a deliberate second gesture, because opening one
                // loses the shape you were reading.
                onClick={(e) => (e.altKey ? openEntity(node.id) : recentre(node.id))}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault()
                    if (e.altKey) openEntity(node.id)
                    else recentre(node.id)
                  }
                }}
              >
                <title>{t('relationships.recentreOn', { name: node.name })}</title>
                {/* Shape and colour together, because five classes on one
                    canvas are unreadable by either alone: a place is square, a
                    thing is a pill, a scene is a cut corner, people stay the
                    rounded box the graph has always drawn. */}
                <rect
                  x={node.x}
                  y={node.y}
                  width={NODE_SIZE.width}
                  height={NODE_SIZE.height}
                  className={`relationships-node type-${node.entityType}`}
                  rx={NODE_RADIUS[node.entityType] ?? 6}
                />
                <text
                  x={node.x + NODE_SIZE.width / 2}
                  y={node.y + NODE_SIZE.height / 2 + 4}
                  className="relationships-nodelabel"
                >
                  {fitLabel(node.name)}
                </text>
                {/* What this person is to the one the graph is centred on.
                    Under the name rather than in a tooltip: the whole reason to
                    centre on somebody is to read this off every node at once. */}
                {kinship[node.id] && (
                  <text
                    x={node.x + NODE_SIZE.width / 2}
                    y={node.y + NODE_SIZE.height - 4}
                    className="relationships-kinship"
                  >
                    {kinship[node.id]}
                  </text>
                )}
              </g>
            ))}
          </svg>
        )}
      </div>
    </div>
  )
}
