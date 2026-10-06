import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { useWikiStore } from '../../stores/wikiStore'
import { useProjectStore } from '../../stores/projectStore'
import { layoutGraph, parentMap, siblingMap, NODE_SIZE, type GraphCharacter } from './layout'
import { kinshipLabel, type KinshipRow } from './kinshipLabel'
import { layoutFamilyTree } from './familyTree'

export function useRelationshipsView() {
  const { t } = useTranslation()
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
  const [depth, setDepth] = useState(2)
  // Scenes as nodes. Novalist always knew which entities appear in which scene
  // and never drew that edge, so "where do these two meet" had no answer here.
  const [withScenes, setWithScenes] = useState(false)
  const {characters, allNodes, kinship, typeOf, chapterOf} = useRelationshipData(rootId, depth, withScenes, t)
  const {search, setSearch, filterGroup, setFilterGroup, filterRole, setFilterRole, hideWorldBible, setHideWorldBible, types, setTypes, availableGroups, availableRoles, hasActiveFilter, clearFilters, filtered} = useRelationshipFilters(characters)
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

  const {zoom, setZoom, pan, setPan, dragRef, movedRef, viewportRef, fitToGraph} = useRelationshipViewport(layout)

  const selected = allNodes.find((node) => node.id === rootId)

  return { fitToGraph, t, search, setSearch, filterGroup, setFilterGroup, availableGroups, filterRole, setFilterRole, availableRoles, types, setTypes, hideWorldBible, setHideWorldBible, rootId, setRootId, allNodes, asTree, depth, setDepth, setAsTree, ancestorDepth, setAncestorDepth, descendantDepth, setDescendantDepth, setTreeHorizontal, treeHorizontal, withScenes, setWithScenes, hasActiveFilter, clearFilters, zoom, viewportRef, pan, setZoom, setPan, dragRef, movedRef, tree, openEntity, recentre, kinship, layout, selected }
}

function useRelationshipData(rootId: string | null, depth: number, withScenes: boolean, t: ReturnType<typeof useTranslation>['t']) {
  const [characters, setCharacters] = useState<GraphCharacter[]>([])
  const [allNodes, setAllNodes] = useState<GraphCharacter[]>([])
  const [kinship, setKinship] = useState<Record<string, string>>({})
  const typeOf = useRef(new Map<string, string>())
  // Scene nodes carry the chapter they are in, so one can be opened rather
  // than only looked at.
  const chapterOf = useRef(new Map<string, string>())
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

  return {characters, allNodes, kinship, typeOf, chapterOf}
}

function useRelationshipFilters(characters: GraphCharacter[]) {
  const [search, setSearch] = useState('')
  const [filterGroup, setFilterGroup] = useState('')
  const [filterRole, setFilterRole] = useState('')
  const [hideWorldBible, setHideWorldBible] = useState(false)
  // Which kinds of entry are on the graph. Characters alone by default: that
  // is what this view has always been, and a first look at a full Codex with
  // everything on at once is unreadable.
  const [types, setTypes] = useState<string[]>(['character'])
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

  return {search, setSearch, filterGroup, setFilterGroup, filterRole, setFilterRole, hideWorldBible, setHideWorldBible, types, setTypes, availableGroups, availableRoles, hasActiveFilter, clearFilters, filtered}
}

function useRelationshipViewport(layout: ReturnType<typeof layoutGraph>) {
  const [zoom, setZoom] = useState(1)
  const [pan, setPan] = useState({ x: 0, y: 0 })
  const dragRef = useRef<{ startX: number; startY: number; panX: number; panY: number } | null>(
    null
  )
  const movedRef = useRef(false)
  const viewportRef = useRef<HTMLDivElement>(null)
  const fitPendingRef = useRef(false)
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

  return {zoom, setZoom, pan, setPan, dragRef, movedRef, viewportRef, fitToGraph}
}

export type RelationshipsViewState = ReturnType<typeof useRelationshipsView>
