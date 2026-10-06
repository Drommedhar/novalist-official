import { type LayoutBox, NODE_W, NODE_H, HORIZ_SPACING, PARTNER_SPACING, VERT_SPACING, FAMILY_TOP, FAMILY_GAP, BOX_PADDING, type Positions, type CoupleChildren, type GraphTopology, type FamilyPlacement, type FamilyRow } from './layoutTypes'

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

function familyGenerations(family: FamilyPlacement): { generation: Map<string, number>; maxGen: number } {
  const { members, memberSet, parentOf, partnerOf } = family
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
  return { generation, maxGen }
}

function groupCoupleChildren(family: FamilyPlacement): Map<string, CoupleChildren> {
  const { members, memberSet, parentOf } = family
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
  return coupleToKids
}

function placeFirstGeneration(family: FamilyPlacement, generation: Map<string, number>, placedAtGen: Map<number, string[]>): void {
  const { members, memberSet, partnerOf, familyLeft, positions } = family
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
}

function centreChildren(family: FamilyPlacement, row: FamilyRow): void {
  const { positions } = family
  const { g, generation, placedAtGen, coupleToKids } = row
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

function placeDescendants(family: FamilyPlacement, generation: Map<string, number>, maxGen: number, coupleToKids: Map<string, CoupleChildren>, placedAtGen: Map<number, string[]>): void {
  const { members, parentOf, familyLeft, positions } = family
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
    centreChildren(family, { g, generation, placedAtGen, coupleToKids })
  }
}

function addFamilyBox(family: FamilyPlacement): number {
  const { members, positions, byId, boxes } = family
  let { familyLeft } = family
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
      label: mostCommon(surnames),
      kind: 'family'
    })
    familyLeft = maxX + BOX_PADDING + FAMILY_GAP
  }
  return familyLeft
}

export function placeFamilies(topology: GraphTopology, positions: Positions, boxes: LayoutBox[], familyLeft: number): CoupleChildren[] {
  const { familyOf, familyCount, parentOf, partnerOf, byId } = topology
  const coupleChildren: CoupleChildren[] = []
  const familyMembers: string[][] = []
  for (let f = 0; f < familyCount; f++) {
    familyMembers.push([...familyOf.entries()].filter(([, fi]) => fi === f).map(([id]) => id))
  }

  for (let f = 0; f < familyCount; f++) {
    const members = familyMembers[f]
    if (members.length === 0) continue
    const memberSet = new Set(members)
    const family = { members, memberSet, parentOf, partnerOf, byId, positions, boxes, familyLeft }
    const { generation, maxGen } = familyGenerations(family)
    const coupleToKids = groupCoupleChildren(family)
    const placedAtGen = new Map<number, string[]>()
    for (let g = 0; g <= maxGen; g++) placedAtGen.set(g, [])
    placeFirstGeneration(family, generation, placedAtGen)
    placeDescendants(family, generation, maxGen, coupleToKids, placedAtGen)
    for (const entry of coupleToKids.values()) coupleChildren.push(entry)
    familyLeft = addFamilyBox(family)
  }
  return coupleChildren
}
