import { type LayoutEdge, type LayoutBox, NODE_W, NODE_H, ROLE_BOX_PADDING, type Positions, type CoupleChildren, type GraphTopology } from './layoutTypes'

/**
 * True when the padded box [minX..maxX] × [minY..maxY] would enclose the centre
 * of any positioned node that is not one of the group's own endpoints. Used to
 * stop greedy role-box expansion from swallowing unrelated characters.
 */
function boxEnvelopsOther(
  positions: Map<string, { x: number; y: number }>,
  endpoints: Set<string>,
  minX: number,
  minY: number,
  maxX: number,
  maxY: number,
  pad: number
): boolean {
  for (const [id, p] of positions) {
    if (endpoints.has(id)) continue
    if (p.x >= minX - pad && p.x <= maxX + pad && p.y >= minY - pad && p.y <= maxY + pad) {
      return true
    }
  }
  return false
}

export function addRoleBoxes(topology: GraphTopology, positions: Positions, boxes: LayoutBox[]): Set<string> {
  const { roleEndpointsByRole, familyOf, edges } = topology
  // Non-family roles shared by three or more characters render as one labeled
  // box instead of a tangle of individual edges. The box wraps the loose
  // (non-family) endpoints, then greedily expands to cover family-side endpoints
  // ordered by X as long as the growth would not swallow an unrelated node.
  // Mirrors the Avalonia RelationshipsGraph role-group boxes.
  const clusteredPairs = new Set<string>()
  for (const [role, eps] of roleEndpointsByRole) {
    if (eps.size < 3) continue
    let minX = Number.POSITIVE_INFINITY
    let minY = Number.POSITIVE_INFINITY
    let maxX = Number.NEGATIVE_INFINITY
    let maxY = Number.NEGATIVE_INFINITY
    for (const id of eps) {
      if (familyOf.has(id)) continue
      const p = positions.get(id)
      if (!p) continue
      minX = Math.min(minX, p.x - NODE_W / 2)
      minY = Math.min(minY, p.y - NODE_H / 2)
      maxX = Math.max(maxX, p.x + NODE_W / 2)
      maxY = Math.max(maxY, p.y + NODE_H / 2)
    }
    if (!Number.isFinite(minX)) continue // no loose endpoints to anchor the box
    const familySide = [...eps]
      .filter((id) => familyOf.has(id) && positions.has(id))
      .sort((a, b) => positions.get(a)!.x - positions.get(b)!.x)
    for (const id of familySide) {
      const p = positions.get(id)!
      const nMinX = Math.min(minX, p.x - NODE_W / 2)
      const nMinY = Math.min(minY, p.y - NODE_H / 2)
      const nMaxX = Math.max(maxX, p.x + NODE_W / 2)
      const nMaxY = Math.max(maxY, p.y + NODE_H / 2)
      if (boxEnvelopsOther(positions, eps, nMinX, nMinY, nMaxX, nMaxY, ROLE_BOX_PADDING)) continue
      minX = nMinX
      minY = nMinY
      maxX = nMaxX
      maxY = nMaxY
    }
    boxes.push({
      x: minX - ROLE_BOX_PADDING,
      y: minY - ROLE_BOX_PADDING,
      width: maxX - minX + 2 * ROLE_BOX_PADDING,
      height: maxY - minY + 2 * ROLE_BOX_PADDING,
      label: role,
      kind: 'role'
    })
    // Suppress every edge of this role so it does not also draw as a line.
    for (const e of edges) {
      if (e.role !== role) continue
      clusteredPairs.add([e.from.id, e.to.id].sort().join('|'))
    }
  }
  return clusteredPairs
}

export function genealogyEdges(coupleChildren: CoupleChildren[], positions: Positions): LayoutEdge[] {
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
      layoutEdges.push({ x1, y1, x2, y2, label: '', labelX: 0, labelY: 0, category: '' })
    }
    seg(parentMidX, parentBottomY, parentMidX, midY) // drop from couple midpoint
    seg(Math.min(parentMidX, kidsMinX), midY, Math.max(parentMidX, kidsMaxX), midY) // bar
    for (const k of presentKids) seg(positions.get(k)!.x, midY, positions.get(k)!.x, childTopY)
  }
  return layoutEdges
}

export function addRelationshipEdges(topology: GraphTopology, positions: Positions, clusteredPairs: Set<string>, layoutEdges: LayoutEdge[]): void {
  const { edges, familyOf, isFamilyRole } = topology
  // Suppress in-family parent/child/partner/sibling edges (implied by the T-tree
  // and family box); merge the rest by unordered pair into one labeled line.
  const merged = new Map<
    string,
    { from: string; to: string; roles: string[]; category: string }
  >()
  for (const e of edges) {
    if (!positions.has(e.from.id) || !positions.has(e.to.id)) continue
    const sameFamily =
      familyOf.has(e.from.id) && familyOf.get(e.from.id) === familyOf.get(e.to.id)
    if (isFamilyRole(e.role) && sameFamily) continue
    const key = [e.from.id, e.to.id].sort().join('|')
    if (clusteredPairs.has(key)) continue // already drawn as a role-group box
    let entry = merged.get(key)
    if (!entry) {
      entry = { from: e.from.id, to: e.to.id, roles: [], category: e.category }
      merged.set(key, entry)
    }
    // Two people can be tied in more than one way; the first typed one colours
    // the line, because a line cannot be two colours and the label lists both.
    if (entry.category.length === 0) entry.category = e.category
    if (!entry.roles.some((r) => r.toLowerCase() === e.role.toLowerCase())) entry.roles.push(e.role)
  }
  for (const { from, to, roles, category } of merged.values()) {
    const a = positions.get(from)!
    const b = positions.get(to)!
    layoutEdges.push({
      x1: a.x,
      y1: a.y,
      x2: b.x,
      y2: b.y,
      label: roles.join(' / '),
      labelX: (a.x + b.x) / 2,
      labelY: (a.y + b.y) / 2 - 4,
      category
    })
  }
}
