import { relationshipRoleKeywords } from '../../i18n'
import { type GraphCharacter, type LayoutNode, type LayoutEdge, type LayoutBox, type GraphLayout, NODE_W, NODE_H, FAMILY_TOP, LEFT_MARGIN, FAMILY_GAP, ROLE_LOOSE_SPACING, PAD, ISOLATED_TOP_GAP, ISOLATED_SPACING_X, ISOLATED_SPACING_Y, type Positions, type GraphTopology } from './layoutTypes'
import { matchesAny, characterNames, graphTopology } from './layoutTopology'
import { placeFamilies } from './familyPlacement'
import { addRoleBoxes, genealogyEdges, addRelationshipEdges } from './layoutEdges'

function preplaceRoleMembers(topology: GraphTopology, positions: Positions): number {
  const { familyOf, roleEndpointsByRole } = topology
  let familyLeft = LEFT_MARGIN
  // Pre-place loose members (not in any family) of multi-endpoint role groups
  // in a compact row at top-left, BEFORE families, so their bounding box stays
  // tight instead of enveloping downstream family clusters. Mirrors the
  // Avalonia RelationshipsGraph pre-placement pass.
  let looseCursorX = LEFT_MARGIN
  for (const eps of roleEndpointsByRole.values()) {
    if (eps.size < 3) continue
    for (const id of eps) {
      if (familyOf.has(id) || positions.has(id)) continue
      positions.set(id, { x: looseCursorX + NODE_W / 2, y: FAMILY_TOP })
      looseCursorX += ROLE_LOOSE_SPACING
    }
  }
  if (positions.size > 0) familyLeft = looseCursorX + FAMILY_GAP
  return familyLeft
}

function placeLooseNodes(nodes: GraphCharacter[], positions: Positions): void {
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
}

function placeIsolatedNodes(isolated: GraphCharacter[], positions: Positions, boxes: LayoutBox[]): void {
  // The untied ones, in rows beneath everything with ties. Kept apart rather
  // than mixed in: they carry no edges, so scattering them among the families
  // would only make the ties harder to follow.
  if (isolated.length > 0) {
    let lowest = FAMILY_TOP
    for (const p of positions.values()) lowest = Math.max(lowest, p.y)
    for (const b of boxes) lowest = Math.max(lowest, b.y + b.height)

    const perRow = Math.max(1, Math.ceil(Math.sqrt(isolated.length)))
    isolated.forEach((node, i) => {
      positions.set(node.id, {
        x: LEFT_MARGIN + (i % perRow) * ISOLATED_SPACING_X + NODE_W / 2,
        y: lowest + ISOLATED_TOP_GAP + Math.floor(i / perRow) * ISOLATED_SPACING_Y
      })
    })
  }
}

function normalizeGraph(nodes: GraphCharacter[], positions: Positions, boxes: LayoutBox[], layoutEdges: LayoutEdge[]): GraphLayout {
  // Materialize nodes (top-left corner) and normalize so all geometry sits at
  // >= PAD, keeping the SVG viewBox origin at (0,0).
  const layoutNodes: LayoutNode[] = nodes
    .filter((n) => positions.has(n.id))
    .map((n) => {
      const p = positions.get(n.id)!
      return {
        id: n.id,
        name: n.displayName,
        x: p.x - NODE_W / 2,
        y: p.y - NODE_H / 2,
        entityType: n.entityType
      }
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

export function layoutGraph(characters: GraphCharacter[]): GraphLayout {
  const topology = graphTopology(characters)
  const positions: Positions = new Map()
  const boxes: LayoutBox[] = []
  const familyLeft = preplaceRoleMembers(topology, positions)
  const coupleChildren = placeFamilies(topology, positions, boxes, familyLeft)
  placeLooseNodes(topology.nodes, positions)
  const clusteredPairs = addRoleBoxes(topology, positions, boxes)
  const edges = genealogyEdges(coupleChildren, positions)
  addRelationshipEdges(topology, positions, clusteredPairs, edges)
  placeIsolatedNodes(topology.isolated, positions, boxes)
  return normalizeGraph(topology.nodes, positions, boxes, edges)
}

export const NODE_SIZE = { width: NODE_W, height: NODE_H }

/**
 * Who each person's parents are, by id.
 *
 * The same classification the family layout does, pulled out so the kinship
 * derivation can use it. Deciding that "Mutter" means a parent is a question
 * about language, which lives here; working out that somebody is therefore a
 * great-aunt is arithmetic, which lives in the backend.
 */
export function parentMap(characters: GraphCharacter[]): Record<string, string[]> {
  const parentWords = relationshipRoleKeywords('parent')
  const childWords = relationshipRoleKeywords('child')

  const byName = characterNames(characters)

  const parents: Record<string, Set<string>> = {}
  const add = (childId: string, parentId: string): void => {
    if (childId === parentId) return
    ;(parents[childId] ??= new Set()).add(parentId)
  }

  for (const c of characters) {
    for (const rel of c.relationships) {
      for (const targetName of rel.target.split(',')) {
        const target = byName.get(targetName.trim().toLowerCase())
        if (!target || target.id === c.id) continue
        // A row names what the TARGET is to this entry: on Liam, "mother ->
        // Amy" means Amy is his mother. Read the other way round it inverted
        // every family - the tree drew parents below their children, and the
        // kinship labels called a mother a daughter.
        if (matchesAny(rel.role, parentWords)) add(c.id, target.id)
        else if (matchesAny(rel.role, childWords)) add(target.id, c.id)
      }
    }
  }

  return Object.fromEntries(Object.entries(parents).map(([id, set]) => [id, [...set]]))
}

/**
 * Who each person's brothers and sisters are, by id.
 *
 * Siblings are normally worked out from a shared parent, which is why the tree
 * did not need this. But a writer who notes "brother: Tom" on a character
 * without ever creating the parents has recorded a family the parent links
 * cannot describe, and reading only those links drew it as a single box. A
 * sibling names a generation directly, so it is worth reading.
 *
 * The link goes both ways: naming somebody your sister makes you hers.
 */
export function siblingMap(characters: GraphCharacter[]): Record<string, string[]> {
  const siblingWords = relationshipRoleKeywords('sibling')

  const byName = characterNames(characters)

  const siblings: Record<string, Set<string>> = {}
  const link = (a: string, b: string): void => {
    if (a === b) return
    ;(siblings[a] ??= new Set()).add(b)
    ;(siblings[b] ??= new Set()).add(a)
  }

  for (const c of characters) {
    for (const rel of c.relationships) {
      if (!matchesAny(rel.role, siblingWords)) continue
      for (const targetName of rel.target.split(',')) {
        const target = byName.get(targetName.trim().toLowerCase())
        if (target) link(c.id, target.id)
      }
    }
  }

  return Object.fromEntries(Object.entries(siblings).map(([id, set]) => [id, [...set]]))
}

export type { GraphCharacter, LayoutNode, LayoutEdge, LayoutBox, GraphLayout } from './layoutTypes'
