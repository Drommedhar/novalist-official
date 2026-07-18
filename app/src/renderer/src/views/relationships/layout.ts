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

export interface LayoutEdge {
  from: string
  to: string
  label: string
  family: boolean
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

const H_SPACING = 130
const V_SPACING = 130
const NODE_W = 90
const NODE_H = 30

function matchesAny(role: string, keywords: Set<string>): boolean {
  const lower = role.toLowerCase()
  for (const keyword of keywords) {
    if (lower.includes(keyword)) return true
  }
  return false
}

/**
 * Family-cluster layout in the spirit of the Avalonia RelationshipsGraph:
 * detect family edges via locale role keywords, cluster them, layer clusters
 * by generation, and place remaining connected characters in a circle below.
 */
export function layoutGraph(characters: GraphCharacter[]): GraphLayout {
  const parentWords = relationshipRoleKeywords('parent')
  const childWords = relationshipRoleKeywords('child')
  const partnerWords = relationshipRoleKeywords('partner')
  const siblingWords = relationshipRoleKeywords('sibling')

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

  const connected = new Set<string>()
  for (const e of edges) {
    connected.add(e.from.id)
    connected.add(e.to.id)
  }
  const nodes = characters.filter((c) => connected.has(c.id))

  // Family adjacency: parent/child/partner (+ sibling) edges cluster into families.
  const parentOf = new Map<string, Set<string>>() // childId -> parentIds
  const partnerOf = new Map<string, Set<string>>()
  const familyAdj = new Map<string, Set<string>>()
  const link = (map: Map<string, Set<string>>, a: string, b: string): void => {
    if (!map.has(a)) map.set(a, new Set())
    map.get(a)!.add(b)
  }
  for (const e of edges) {
    const isParent = matchesAny(e.role, parentWords)
    const isChild = matchesAny(e.role, childWords)
    const isPartner = !isParent && !isChild && matchesAny(e.role, partnerWords)
    const isSibling = !isParent && !isChild && !isPartner && matchesAny(e.role, siblingWords)
    // "role" reads as from's role toward to: from is to's <role>.
    if (isParent) link(parentOf, e.to.id, e.from.id)
    if (isChild) link(parentOf, e.from.id, e.to.id)
    if (isPartner) {
      link(partnerOf, e.from.id, e.to.id)
      link(partnerOf, e.to.id, e.from.id)
    }
    if (isParent || isChild || isPartner || isSibling) {
      link(familyAdj, e.from.id, e.to.id)
      link(familyAdj, e.to.id, e.from.id)
    }
  }

  // Cluster families via DFS.
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

  // Generation = longest parent chain, partners pulled to the same generation.
  const generation = new Map<string, number>()
  const computeGen = (id: string, seen: Set<string>): number => {
    if (generation.has(id)) return generation.get(id)!
    if (seen.has(id)) return 0
    seen.add(id)
    const parents = parentOf.get(id)
    const gen =
      parents && parents.size > 0
        ? Math.max(...[...parents].map((p) => computeGen(p, seen))) + 1
        : 0
    generation.set(id, gen)
    return gen
  }
  for (const id of familyOf.keys()) computeGen(id, new Set())
  for (const [id, partners] of partnerOf) {
    for (const partner of partners) {
      const gen = Math.max(generation.get(id) ?? 0, generation.get(partner) ?? 0)
      generation.set(id, gen)
      generation.set(partner, gen)
    }
  }

  const positioned = new Map<string, { x: number; y: number }>()
  const boxes: LayoutBox[] = []
  let familyLeft = 40

  const byId = new Map(nodes.map((n) => [n.id, n]))
  for (let family = 0; family < familyCount; family++) {
    const members = [...familyOf.entries()].filter(([, f]) => f === family).map(([id]) => id)
    if (members.length === 0) continue
    const generations = new Map<number, string[]>()
    for (const id of members) {
      const gen = generation.get(id) ?? 0
      if (!generations.has(gen)) generations.set(gen, [])
      generations.get(gen)!.push(id)
    }
    let maxRow = 0
    for (const [gen, ids] of [...generations.entries()].sort((a, b) => a[0] - b[0])) {
      ids.sort((a, b) => (byId.get(a)?.displayName ?? '').localeCompare(byId.get(b)?.displayName ?? ''))
      ids.forEach((id, index) => {
        positioned.set(id, {
          x: familyLeft + index * H_SPACING,
          y: 60 + gen * V_SPACING
        })
      })
      maxRow = Math.max(maxRow, ids.length)
    }
    const width = Math.max(1, maxRow) * H_SPACING
    const height = generations.size * V_SPACING
    const surnames = members
      .map((id) => byId.get(id)?.surname ?? '')
      .filter((s) => s.length > 0)
    const surname = mostCommon(surnames)
    boxes.push({
      x: familyLeft - 20,
      y: 30,
      width: width + 10,
      height: height + 20,
      label: surname ? `${surname}` : ''
    })
    familyLeft += width + 80
  }

  // Loose connected nodes without a family: circle below the family row.
  const loose = nodes.filter((n) => !positioned.has(n.id))
  const maxFamilyBottom = boxes.reduce((max, b) => Math.max(max, b.y + b.height), 60)
  loose.forEach((node, index) => {
    const angle = (index / Math.max(1, loose.length)) * Math.PI * 2
    positioned.set(node.id, {
      x: 240 + Math.cos(angle) * (60 + loose.length * 14),
      y: maxFamilyBottom + 160 + Math.sin(angle) * (60 + loose.length * 14)
    })
  })

  // Merge pairwise edges; family edges within a cluster are drawn thin.
  const merged = new Map<string, LayoutEdge>()
  for (const e of edges) {
    const key = [e.from.id, e.to.id].sort().join('|')
    const isFamily =
      matchesAny(e.role, parentWords) ||
      matchesAny(e.role, childWords) ||
      matchesAny(e.role, partnerWords) ||
      matchesAny(e.role, siblingWords)
    const existing = merged.get(key)
    if (existing) {
      if (!existing.label.includes(e.role)) existing.label = `${existing.label} / ${e.role}`
      existing.family = existing.family && isFamily
    } else {
      merged.set(key, { from: e.from.id, to: e.to.id, label: e.role, family: isFamily })
    }
  }

  const layoutNodes: LayoutNode[] = nodes.map((n) => ({
    id: n.id,
    name: n.displayName,
    x: positioned.get(n.id)?.x ?? 0,
    y: positioned.get(n.id)?.y ?? 0
  }))
  const width = Math.max(600, ...layoutNodes.map((n) => n.x + NODE_W + 60))
  const height = Math.max(400, ...layoutNodes.map((n) => n.y + NODE_H + 60))
  return { nodes: layoutNodes, edges: [...merged.values()], boxes, width, height }
}

export const NODE_SIZE = { width: NODE_W, height: NODE_H }

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
