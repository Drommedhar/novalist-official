import type { GraphCharacter } from './layout'

export interface TreeNode {
  id: string
  name: string
  /** 0 is the root, negative is an ancestor, positive is a descendant. */
  generation: number
  x: number
  y: number
}

export interface TreeEdge {
  parentId: string
  childId: string
}

export interface TreeLayout {
  nodes: TreeNode[]
  edges: TreeEdge[]
  width: number
  height: number
}

/** Room for a name plus the gap that keeps two of them apart. */
export const TREE_NODE_WIDTH = 132
export const TREE_NODE_HEIGHT = 34
const GAP_ACROSS = 26
const GAP_BETWEEN = 62
const MARGIN = 30

/**
 * A family as generations rather than as a graph.
 *
 * The Relationships canvas lays everything out by force, which is right when
 * the question is "what is connected to what" and wrong when it is "who
 * descends from whom": a force layout puts a grandmother wherever there is
 * room, so three generations read as a cloud. A tree puts a generation on a
 * line, which is the one thing a family view has to do.
 *
 * Ancestors go one way from the root and descendants the other, each to its own
 * depth, because a writer tracing a line of succession wants ten generations
 * down and one up, and the same view with both at ten is unreadable.
 *
 * A family is not a line, though. Walking strictly up and then strictly down
 * from the root reaches only the people the root descends from and the people
 * who descend from the root, which leaves out every branch: no brothers, no
 * aunts, no cousins, no nieces. What a family tree shows is the line of ascent
 * and then everyone descending from it - so the walk goes up first, and comes
 * back down from every ancestor it found.
 */
export function layoutFamilyTree(
  characters: GraphCharacter[],
  parents: Record<string, string[]>,
  rootId: string,
  options: { ancestors: number; descendants: number; horizontal: boolean },
  siblings: Record<string, string[]> = {}
): TreeLayout {
  const byId = new Map(characters.map((c) => [c.id, c]))
  if (!byId.has(rootId)) return { nodes: [], edges: [], width: 0, height: 0 }

  const children: Record<string, string[]> = {}
  for (const [childId, parentIds] of Object.entries(parents)) {
    for (const parentId of parentIds) (children[parentId] ??= []).push(childId)
  }

  const upTo = Math.max(0, options.ancestors)
  const downTo = Math.max(0, options.descendants)

  // Generation per id. First writer wins, so somebody reachable both ways -
  // which a cousin marriage produces - is placed once rather than twice.
  const generation = new Map<string, number>([[rootId, 0]])

  // Up the root's own line, and only that line. Following parents from anyone
  // met later would climb into the family somebody married into and bring their
  // whole ancestry along, which is a different family's tree.
  let frontier = [rootId]
  for (let level = 1; level <= upTo; level++) {
    const next: string[] = []
    for (const id of frontier) {
      for (const parentId of parents[id] ?? []) {
        if (generation.has(parentId) || !byId.has(parentId)) continue
        generation.set(parentId, -level)
        next.push(parentId)
      }
    }
    if (next.length === 0) break
    frontier = next
  }

  // Down from every one of them - which is what turns a line into a family.
  // Descending from the great-grandmother reaches great-aunts, their children
  // and their children's children, all of them the root's relatives.
  const queue = [...generation.keys()]
  while (queue.length > 0) {
    const id = queue.shift()!
    const level = generation.get(id)!
    // A brother named on the entry with no parents recorded anywhere sits on
    // the same row, since that is the whole of what is known about him.
    for (const siblingId of siblings[id] ?? []) {
      if (generation.has(siblingId) || !byId.has(siblingId)) continue
      generation.set(siblingId, level)
      queue.push(siblingId)
    }
    if (level + 1 > downTo) continue
    for (const childId of children[id] ?? []) {
      if (generation.has(childId) || !byId.has(childId)) continue
      generation.set(childId, level + 1)
      queue.push(childId)
    }
  }

  // One row per generation, in reading order so the tree does not reshuffle
  // between renders.
  const rows = new Map<number, string[]>()
  for (const character of characters) {
    const level = generation.get(character.id)
    if (level === undefined) continue
    const row = rows.get(level)
    if (row) row.push(character.id)
    else rows.set(level, [character.id])
  }

  const levels = [...rows.keys()].sort((a, b) => a - b)
  const widest = Math.max(...levels.map((l) => rows.get(l)!.length), 1)
  const across = widest * (TREE_NODE_WIDTH + GAP_ACROSS)
  const along = levels.length * (TREE_NODE_HEIGHT + GAP_BETWEEN)

  const nodes: TreeNode[] = []
  levels.forEach((level, levelIndex) => {
    const row = rows.get(level)!
    const rowWidth = row.length * (TREE_NODE_WIDTH + GAP_ACROSS)
    row.forEach((id, index) => {
      // Rows are centred against the widest one, or a generation of two hangs
      // off the left of a generation of nine and reads as unrelated.
      const offset = (across - rowWidth) / 2 + index * (TREE_NODE_WIDTH + GAP_ACROSS)
      const step = levelIndex * (TREE_NODE_HEIGHT + GAP_BETWEEN)
      nodes.push({
        id,
        name: byId.get(id)!.displayName || byId.get(id)!.name,
        generation: level,
        x: MARGIN + (options.horizontal ? step : offset),
        y: MARGIN + (options.horizontal ? offset : step)
      })
    })
  })

  const placed = new Set(nodes.map((n) => n.id))
  const edges: TreeEdge[] = []
  for (const [childId, parentIds] of Object.entries(parents)) {
    if (!placed.has(childId)) continue
    for (const parentId of parentIds) {
      if (placed.has(parentId)) edges.push({ parentId, childId })
    }
  }

  return {
    nodes,
    edges,
    width: MARGIN * 2 + (options.horizontal ? along : across),
    height: MARGIN * 2 + (options.horizontal ? across : along)
  }
}
