import { relationshipRoleKeywords } from '../../i18n'
import { type GraphCharacter, type Edge, type FamilyRoleWords, type FamilyLinks, type GraphTopology } from './layoutTypes'

export function matchesAny(role: string, keywords: Set<string>): boolean {
  const lower = role.toLowerCase()
  for (const keyword of keywords) {
    if (lower.includes(keyword)) return true
  }
  return false
}

export function characterNames(characters: GraphCharacter[]): Map<string, GraphCharacter> {
  const byName = new Map<string, GraphCharacter>()
  for (const c of characters) {
    byName.set(c.displayName.toLowerCase(), c)
    if (!byName.has(c.name.toLowerCase())) byName.set(c.name.toLowerCase(), c)
  }
  return byName
}

function resolveGraphEdges(characters: GraphCharacter[]): Edge[] {
  const byName = characterNames(characters)
  const edges: Edge[] = []
  for (const c of characters) {
    for (const rel of c.relationships) {
      for (const targetName of rel.target.split(',')) {
        const target = byName.get(targetName.trim().toLowerCase())
        if (target && target.id !== c.id)
          edges.push({ from: c, to: target, role: rel.role, category: rel.category ?? '' })
      }
    }
  }
  return edges
}

function classifyFamilyEdges(edges: Edge[], byId: Map<string, GraphCharacter>, words: FamilyRoleWords): FamilyLinks {
  const { parentWords, childWords, partnerWords, siblingWords } = words
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
  return { parentOf, childrenOf, partnerOf, familyAdj }
}

function clusterFamilies(nodes: GraphCharacter[], familyAdj: Map<string, Set<string>>): { familyOf: Map<string, number>; familyCount: number } {
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
  return { familyOf, familyCount }
}

function roleEndpoints(edges: Edge[], byId: Map<string, GraphCharacter>, isFamilyRole: (role: string) => boolean): Map<string, Set<string>> {
  // Endpoints of each non-family role (node ids on either side of the edge).
  // A role shared by >=3 characters is drawn later as one labeled box.
  const roleEndpointsByRole = new Map<string, Set<string>>()
  for (const e of edges) {
    if (!byId.has(e.from.id) || !byId.has(e.to.id)) continue
    if (isFamilyRole(e.role)) continue
    let set = roleEndpointsByRole.get(e.role)
    if (!set) {
      set = new Set()
      roleEndpointsByRole.set(e.role, set)
    }
    set.add(e.from.id)
    set.add(e.to.id)
  }
  return roleEndpointsByRole
}

export function graphTopology(characters: GraphCharacter[]): GraphTopology {
  const parentWords = relationshipRoleKeywords('parent')
  const childWords = relationshipRoleKeywords('child')
  const partnerWords = relationshipRoleKeywords('partner')
  const siblingWords = relationshipRoleKeywords('sibling')
  const isFamilyRole = (role: string): boolean =>
    matchesAny(role, parentWords) ||
    matchesAny(role, childWords) ||
    matchesAny(role, partnerWords) ||
    matchesAny(role, siblingWords)
  const edges = resolveGraphEdges(characters)
  const connectedIds = new Set<string>()
  for (const e of edges) {
    connectedIds.add(e.from.id)
    connectedIds.add(e.to.id)
  }
  const nodes = characters
  const connected = characters.filter((c) => connectedIds.has(c.id))
  const isolated = characters.filter((c) => !connectedIds.has(c.id))
  const byId = new Map(connected.map((n) => [n.id, n]))
  const links = classifyFamilyEdges(edges, byId, { parentWords, childWords, partnerWords, siblingWords })
  const { familyOf, familyCount } = clusterFamilies(nodes, links.familyAdj)
  const roleEndpointsByRole = roleEndpoints(edges, byId, isFamilyRole)
  return { nodes, isolated, edges, byId, ...links, familyOf, familyCount, roleEndpointsByRole, isFamilyRole }
}
