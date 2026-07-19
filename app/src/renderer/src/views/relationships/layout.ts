import { relationshipRoleKeywords } from '../../i18n'

export interface GraphCharacter {
  id: string
  name: string
  displayName: string
  surname: string
  group: string
  role: string
  isWorldBible: boolean
  relationships: { role: string; target: string }[]
}

export interface LayoutNode {
  id: string
  name: string
  x: number
  y: number
}

/**
 * An explicit line segment in graph coordinates. Family (parent/child)
 * relationships render as unlabeled genealogy T-connector segments; other
 * relationships render as a single labeled segment between two node centres.
 */
export interface LayoutEdge {
  x1: number
  y1: number
  x2: number
  y2: number
  label: string
  labelX: number
  labelY: number
}

export interface LayoutBox {
  x: number
  y: number
  width: number
  height: number
  label: string
}

export interface GraphLayout {
  nodes: LayoutNode[]
  edges: LayoutEdge[]
  boxes: LayoutBox[]
  width: number
  height: number
}

const NODE_W = 90
const NODE_H = 30
const HORIZ_SPACING = 140 // gap between siblings / unrelated members in a row
const PARTNER_SPACING = 140 // gap between paired partners
const VERT_SPACING = 140 // gap between generation rows
const FAMILY_TOP = 80
const LEFT_MARGIN = 60
const FAMILY_GAP = 100
const BOX_PADDING = 20
const PAD = 40 // normalized canvas margin

function matchesAny(role: string, keywords: Set<string>): boolean {
  const lower = role.toLowerCase()
  for (const keyword of keywords) {
    if (lower.includes(keyword)) return true
  }
  return false
}

function mostCommon(values: string[]): string {
  const counts = new Map<string, number>()
  for (const value of values) counts.set(value, (counts.get(value) ?? 0) + 1)
  let best = ''
  let bestCount = 0
  for (const [value, count] of counts) {
    if (count > bestCount) {
      best = value
      bestCount = count
    }
  }
  return best
}

/**
 * Family-genealogy layout mirroring the Avalonia RelationshipsGraph: detect
 * family edges via locale role keywords, cluster them into families, lay each
 * family out row-per-generation with children centred under their parents'
 * midpoint, and draw parent→child links as T-connectors (vertical from the
 * couple midpoint down to a horizontal bar, then a drop to each child).
 * Non-family relationships remain merged, labeled node-to-node lines and
 * loose connected characters cluster in a circle below the families.
 */
export function layoutGraph(characters: GraphCharacter[]): GraphLayout {
  const parentWords = relationshipRoleKeywords('parent')
  const childWords = relationshipRoleKeywords('child')
  const partnerWords = relationshipRoleKeywords('partner')
  const siblingWords = relationshipRoleKeywords('sibling')
  const isFamilyRole = (role: string): boolean =>
    matchesAny(role, parentWords) ||
    matchesAny(role, childWords) ||
    matchesAny(role, partnerWords) ||
    matchesAny(role, siblingWords)

  const byName = new Map<string, GraphCharacter>()
  for (const c of characters) {
    byName.set(c.displayName.toLowerCase(), c)
    if (!byName.has(c.name.toLowerCase())) byName.set(c.name.toLowerCase(), c)
  }

  interface Edge {
    from: GraphCharacter
    to: GraphCharacter
    role: string
  }
  const edges: Edge[] = []
  for (const c of characters) {
    for (const rel of c.relationships) {
      for (const targetName of rel.target.split(',')) {
        const target = byName.get(targetName.trim().toLowerCase())
        if (target && target.id !== c.id) edges.push({ from: c, to: target, role: rel.role })
      }
    }
  }

  const connectedIds = new Set<string>()
  for (const e of edges) {
    connectedIds.add(e.from.id)
    connectedIds.add(e.to.id)
  }
  const nodes = characters.filter((c) => connectedIds.has(c.id))
  const byId = new Map(nodes.map((n) => [n.id, n]))

  // Family adjacency: parent→child, child→parent, partner pairs, siblings.
  const parentOf = new Map<string, Set<string>>() // childId -> parentIds
  const childrenOf = new Map<string, Set<string>>() // parentId -> childIds
  const partnerOf = new Map<string, Set<string>>()
  const familyAdj = new Map<string, Set<string>>()
  const link = (map: Map<string, Set<string>>, a: string, b: string): void => {
    let set = map.get(a)
    if (!set) {
      set = new Set()
      map.set(a, set)
    }
    set.add(b)
  }
  const addPartner = (a: string, b: string): void => {
    link(partnerOf, a, b)
    link(partnerOf, b, a)
  }
  for (const e of edges) {
    if (!byId.has(e.from.id) || !byId.has(e.to.id)) continue
    const isParent = matchesAny(e.role, parentWords)
    const isChild = matchesAny(e.role, childWords)
    const isPartner = !isParent && !isChild && matchesAny(e.role, partnerWords)
    const isSibling = !isParent && !isChild && !isPartner && matchesAny(e.role, siblingWords)
    // "role" reads as from's role toward to: from is to's <role>.
    if (isParent) {
      link(parentOf, e.to.id, e.from.id)
      link(childrenOf, e.from.id, e.to.id)
    }
    if (isChild) {
      link(parentOf, e.from.id, e.to.id)
      link(childrenOf, e.to.id, e.from.id)
    }
    if (isPartner) addPartner(e.from.id, e.to.id)
    if (isParent || isChild || isPartner || isSibling) {
      link(familyAdj, e.from.id, e.to.id)
      link(familyAdj, e.to.id, e.from.id)
    }
  }

  // Co-parents (share at least one child) are implicit partners so they sit
  // adjacent above their shared children.
  for (const [parent, kids] of childrenOf) {
    for (const kid of kids) {
      for (const coParent of parentOf.get(kid) ?? []) {
        if (coParent !== parent) addPartner(parent, coParent)
      }
    }
  }

  // Cluster families via DFS over the family adjacency.
  const familyOf = new Map<string, number>()
  let familyCount = 0
  for (const node of nodes) {
    if (familyOf.has(node.id) || !familyAdj.has(node.id)) continue
    const stack = [node.id]
    while (stack.length > 0) {
      const current = stack.pop()!
      if (familyOf.has(current)) continue
      familyOf.set(current, familyCount)
      for (const next of familyAdj.get(current) ?? []) stack.push(next)
    }
    familyCount++
  }

  // positions store node CENTRE coordinates.
  const positions = new Map<string, { x: number; y: number }>()
  const boxes: LayoutBox[] = []
  const coupleChildren: { parents: string[]; children: string[] }[] = []
  let familyLeft = LEFT_MARGIN

  const familyMembers: string[][] = []
  for (let f = 0; f < familyCount; f++) {
    familyMembers.push([...familyOf.entries()].filter(([, fi]) => fi === f).map(([id]) => id))
  }

  for (let f = 0; f < familyCount; f++) {
    const members = familyMembers[f]
    if (members.length === 0) continue
    const memberSet = new Set(members)

    // Generation = longest parent chain inside the cluster.
    const generation = new Map<string, number>()
    const computeGen = (id: string, seen: Set<string>): number => {
      const cached = generation.get(id)
      if (cached !== undefined) return cached
      if (seen.has(id)) {
        generation.set(id, 0)
        return 0
      }
      seen.add(id)
      let max = 0
      for (const p of parentOf.get(id) ?? []) {
        if (memberSet.has(p)) max = Math.max(max, computeGen(p, seen) + 1)
      }
      generation.set(id, max)
      return max
    }
    for (const m of members) computeGen(m, new Set())

    // Partners share a generation: pull the earlier partner down until stable.
    let changed = true
    while (changed) {
      changed = false
      for (const m of members) {
        for (const p of partnerOf.get(m) ?? []) {
          if (!memberSet.has(p)) continue
          if ((generation.get(p) ?? 0) < (generation.get(m) ?? 0)) {
            generation.set(p, generation.get(m) ?? 0)
            changed = true
          }
        }
      }
    }
    let maxGen = 0
    for (const m of members) maxGen = Math.max(maxGen, generation.get(m) ?? 0)

    // Couple → children groups (children sharing the same set of in-cluster parents).
    const coupleToKids = new Map<string, { parents: string[]; children: string[] }>()
    for (const m of members) {
      const inCluster = [...(parentOf.get(m) ?? [])].filter((p) => memberSet.has(p)).sort()
      if (inCluster.length === 0) continue
      const key = inCluster.join('|')
      let entry = coupleToKids.get(key)
      if (!entry) {
        entry = { parents: inCluster, children: [] }
        coupleToKids.set(key, entry)
      }
      entry.children.push(m)
    }

    const placedAtGen = new Map<number, string[]>()
    for (let g = 0; g <= maxGen; g++) placedAtGen.set(g, [])

    // Generation 0: place partner units adjacent, singletons after.
    const gen0 = members.filter((m) => (generation.get(m) ?? 0) === 0)
    const visited = new Set<string>()
    const gen0Units: string[][] = []
    for (const m of gen0) {
      if (visited.has(m)) continue
      const unit = [m]
      visited.add(m)
      for (const p of [...(partnerOf.get(m) ?? [])].sort()) {
        if (!visited.has(p) && memberSet.has(p) && (generation.get(p) ?? 0) === 0) {
          unit.push(p)
          visited.add(p)
        }
      }
      gen0Units.push(unit)
    }
    let cursorX = familyLeft
    for (const unit of gen0Units) {
      unit.forEach((id, i) => {
        positions.set(id, { x: cursorX + i * PARTNER_SPACING, y: FAMILY_TOP })
        placedAtGen.get(0)!.push(id)
      })
      cursorX += unit.length * PARTNER_SPACING + HORIZ_SPACING * 0.4
    }

    // Subsequent generations: each member desires its parents' average X.
    for (let g = 1; g <= maxGen; g++) {
      const rowMembers = members.filter((m) => (generation.get(m) ?? 0) === g)
      const desired = rowMembers.map((m) => {
        let sum = 0
        let count = 0
        for (const p of parentOf.get(m) ?? []) {
          const pp = positions.get(p)
          if (pp) {
            sum += pp.x
            count++
          }
        }
        return { id: m, x: count > 0 ? sum / count : familyLeft }
      })
      desired.sort((a, b) => a.x - b.x)
      let prevX = Number.NEGATIVE_INFINITY
      const rowY = FAMILY_TOP + g * VERT_SPACING
      for (const { id, x } of desired) {
        const px = Math.max(x, prevX + HORIZ_SPACING)
        positions.set(id, { x: px, y: rowY })
        placedAtGen.get(g)!.push(id)
        prevX = px
      }

      // Re-centre each couple's children under the parents' midpoint when the
      // shift does not collide with other nodes on the same row.
      for (const entry of coupleToKids.values()) {
        const kidsInRow = entry.children.filter((k) => (generation.get(k) ?? 0) === g)
        if (kidsInRow.length === 0) continue
        if (!entry.parents.every((p) => positions.has(p))) continue
        const parentMidX = entry.parents.reduce((s, p) => s + positions.get(p)!.x, 0) / entry.parents.length
        const kidsAvg = kidsInRow.reduce((s, k) => s + positions.get(k)!.x, 0) / kidsInRow.length
        const delta = parentMidX - kidsAvg
        if (Math.abs(delta) < 1) continue
        const kidSet = new Set(kidsInRow)
        const blockMinX = Math.min(...kidsInRow.map((k) => positions.get(k)!.x)) + delta
        const blockMaxX = Math.max(...kidsInRow.map((k) => positions.get(k)!.x)) + delta
        const rowSorted = placedAtGen
          .get(g)!
          .map((id) => ({ id, x: positions.get(id)!.x }))
          .sort((a, b) => a.x - b.x)
        const canShift = rowSorted.every(
          ({ id, x }) => kidSet.has(id) || x < blockMinX - HORIZ_SPACING || x > blockMaxX + HORIZ_SPACING
        )
        if (canShift) {
          for (const k of kidsInRow) {
            const p = positions.get(k)!
            positions.set(k, { x: p.x + delta, y: p.y })
          }
        }
      }
    }

    for (const entry of coupleToKids.values()) coupleChildren.push(entry)

    // Family bounding box + surname label.
    let minX = Number.POSITIVE_INFINITY
    let minY = Number.POSITIVE_INFINITY
    let maxX = Number.NEGATIVE_INFINITY
    let maxY = Number.NEGATIVE_INFINITY
    for (const m of members) {
      const p = positions.get(m)
      if (!p) continue
      minX = Math.min(minX, p.x - NODE_W / 2)
      minY = Math.min(minY, p.y - NODE_H / 2)
      maxX = Math.max(maxX, p.x + NODE_W / 2)
      maxY = Math.max(maxY, p.y + NODE_H / 2)
    }
    if (Number.isFinite(minX)) {
      const surnames = members.map((m) => byId.get(m)?.surname ?? '').filter((s) => s.length > 0)
      boxes.push({
        x: minX - BOX_PADDING,
        y: minY - BOX_PADDING,
        width: maxX - minX + 2 * BOX_PADDING,
        height: maxY - minY + 2 * BOX_PADDING,
        label: mostCommon(surnames)
      })
      familyLeft = maxX + BOX_PADDING + FAMILY_GAP
    }
  }

  // Loose connected nodes (no family) cluster in a circle below the families.
  const loose = nodes.filter((n) => !positions.has(n.id))
  if (loose.length > 0) {
    let maxFamilyY = FAMILY_TOP
    for (const p of positions.values()) maxFamilyY = Math.max(maxFamilyY, p.y)
    const centerX = 480
    const centerY = maxFamilyY + 250
    const radius = Math.min(320, 100 + loose.length * 14)
    loose.forEach((node, i) => {
      const angle = (i * 2 * Math.PI) / loose.length - Math.PI / 2
      positions.set(node.id, {
        x: centerX + radius * Math.cos(angle),
        y: centerY + radius * Math.sin(angle)
      })
    })
  }

  const layoutEdges: LayoutEdge[] = []

  // Genealogy T-connectors from each couple down to their children.
  for (const { parents, children } of coupleChildren) {
    const presentParents = parents.filter((p) => positions.has(p))
    const presentKids = children.filter((k) => positions.has(k))
    if (presentParents.length === 0 || presentKids.length === 0) continue
    const parentBottomY = Math.max(...presentParents.map((p) => positions.get(p)!.y)) + NODE_H / 2
    const childTopY = Math.min(...presentKids.map((k) => positions.get(k)!.y)) - NODE_H / 2
    const midY = (parentBottomY + childTopY) / 2
    const parentMidX = presentParents.reduce((s, p) => s + positions.get(p)!.x, 0) / presentParents.length
    const kidsMinX = Math.min(...presentKids.map((k) => positions.get(k)!.x))
    const kidsMaxX = Math.max(...presentKids.map((k) => positions.get(k)!.x))
    const seg = (x1: number, y1: number, x2: number, y2: number): void => {
      layoutEdges.push({ x1, y1, x2, y2, label: '', labelX: 0, labelY: 0 })
    }
    seg(parentMidX, parentBottomY, parentMidX, midY) // drop from couple midpoint
    seg(Math.min(parentMidX, kidsMinX), midY, Math.max(parentMidX, kidsMaxX), midY) // bar
    for (const k of presentKids) seg(positions.get(k)!.x, midY, positions.get(k)!.x, childTopY)
  }

  // Suppress in-family parent/child/partner/sibling edges (implied by the T-tree
  // and family box); merge the rest by unordered pair into one labeled line.
  const merged = new Map<string, { from: string; to: string; roles: string[] }>()
  for (const e of edges) {
    if (!positions.has(e.from.id) || !positions.has(e.to.id)) continue
    const sameFamily =
      familyOf.has(e.from.id) && familyOf.get(e.from.id) === familyOf.get(e.to.id)
    if (isFamilyRole(e.role) && sameFamily) continue
    const key = [e.from.id, e.to.id].sort().join('|')
    let entry = merged.get(key)
    if (!entry) {
      entry = { from: e.from.id, to: e.to.id, roles: [] }
      merged.set(key, entry)
    }
    if (!entry.roles.some((r) => r.toLowerCase() === e.role.toLowerCase())) entry.roles.push(e.role)
  }
  for (const { from, to, roles } of merged.values()) {
    const a = positions.get(from)!
    const b = positions.get(to)!
    layoutEdges.push({
      x1: a.x,
      y1: a.y,
      x2: b.x,
      y2: b.y,
      label: roles.join(' / '),
      labelX: (a.x + b.x) / 2,
      labelY: (a.y + b.y) / 2 - 4
    })
  }

  // Materialize nodes (top-left corner) and normalize so all geometry sits at
  // >= PAD, keeping the SVG viewBox origin at (0,0).
  const layoutNodes: LayoutNode[] = nodes
    .filter((n) => positions.has(n.id))
    .map((n) => {
      const p = positions.get(n.id)!
      return { id: n.id, name: n.displayName, x: p.x - NODE_W / 2, y: p.y - NODE_H / 2 }
    })

  let minX = Number.POSITIVE_INFINITY
  let minY = Number.POSITIVE_INFINITY
  for (const n of layoutNodes) {
    minX = Math.min(minX, n.x)
    minY = Math.min(minY, n.y)
  }
  for (const b of boxes) {
    minX = Math.min(minX, b.x)
    minY = Math.min(minY, b.y)
  }
  for (const e of layoutEdges) {
    minX = Math.min(minX, e.x1, e.x2)
    minY = Math.min(minY, e.y1, e.y2)
  }
  if (!Number.isFinite(minX)) {
    minX = 0
    minY = 0
  }
  const dx = PAD - minX
  const dy = PAD - minY
  for (const n of layoutNodes) {
    n.x += dx
    n.y += dy
  }
  for (const b of boxes) {
    b.x += dx
    b.y += dy
  }
  for (const e of layoutEdges) {
    e.x1 += dx
    e.y1 += dy
    e.x2 += dx
    e.y2 += dy
    e.labelX += dx
    e.labelY += dy
  }

  let width = 600
  let height = 400
  for (const n of layoutNodes) {
    width = Math.max(width, n.x + NODE_W + PAD)
    height = Math.max(height, n.y + NODE_H + PAD)
  }
  for (const b of boxes) {
    width = Math.max(width, b.x + b.width + PAD)
    height = Math.max(height, b.y + b.height + PAD)
  }
  return { nodes: layoutNodes, edges: layoutEdges, boxes, width, height }
}

export const NODE_SIZE = { width: NODE_W, height: NODE_H }
