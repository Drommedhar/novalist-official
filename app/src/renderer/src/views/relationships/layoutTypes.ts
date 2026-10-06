

export interface GraphCharacter {
  id: string
  name: string
  displayName: string
  surname: string
  group: string
  role: string
  isWorldBible: boolean
  relationships: { role: string; target: string; category: string }[]
  /** character, location, item, lore, or a custom type key. */
  entityType: string
  /** For a scene node, the chapter it is in. Absent for every other kind. */
  chapterGuid?: string | null
}

export interface LayoutNode {
  id: string
  name: string
  x: number
  y: number
  /**
   * What kind of entry this is, so the graph can draw a scene differently from
   * a person. Five classes on one canvas are unreadable when every one of them
   * is the same rounded box.
   */
  entityType: string
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
  /**
   * What kind of tie this is, for colour. Empty on the genealogy connectors,
   * which are structure rather than a relationship anyone wrote, and on ties
   * written before edges could be typed.
   */
  category: string
}

export interface LayoutBox {
  x: number
  y: number
  width: number
  height: number
  label: string
  /**
   * 'family' boxes wrap a genealogy cluster and their label is a surname;
   * 'role' boxes wrap the endpoints of a non-family relationship shared by
   * three or more characters (e.g. "Ring") and their label is the raw role.
   */
  kind: 'family' | 'role'
}

export interface GraphLayout {
  nodes: LayoutNode[]
  edges: LayoutEdge[]
  boxes: LayoutBox[]
  width: number
  height: number
}

export const NODE_W = 90
export const NODE_H = 30
export const HORIZ_SPACING = 140 // gap between siblings / unrelated members in a row
export const PARTNER_SPACING = 140 // gap between paired partners
export const VERT_SPACING = 140 // gap between generation rows
export const FAMILY_TOP = 80
export const LEFT_MARGIN = 60
export const FAMILY_GAP = 100
export const BOX_PADDING = 20
export const ROLE_BOX_PADDING = 14
export const ROLE_LOOSE_SPACING = 120 // gap between pre-placed loose role-group members
export const PAD = 40 // normalized canvas margin
// Entries with no ties yet, laid out in a block of their own below the graph.
export const ISOLATED_TOP_GAP = 120
export const ISOLATED_SPACING_X = 120
export const ISOLATED_SPACING_Y = 60

export interface Edge {
  from: GraphCharacter
  to: GraphCharacter
  role: string
  /** What kind of tie it is, for colour; empty when it was never typed. */
  category: string
}

export type Positions = Map<string, { x: number; y: number }>

export type CoupleChildren = { parents: string[]; children: string[] }

export interface FamilyRoleWords {
  parentWords: Set<string>
  childWords: Set<string>
  partnerWords: Set<string>
  siblingWords: Set<string>
}

export interface FamilyLinks {
  parentOf: Map<string, Set<string>>
  childrenOf: Map<string, Set<string>>
  partnerOf: Map<string, Set<string>>
  familyAdj: Map<string, Set<string>>
}

export interface GraphTopology extends FamilyLinks {
  nodes: GraphCharacter[]
  isolated: GraphCharacter[]
  edges: Edge[]
  byId: Map<string, GraphCharacter>
  familyOf: Map<string, number>
  familyCount: number
  roleEndpointsByRole: Map<string, Set<string>>
  isFamilyRole: (role: string) => boolean
}

export interface FamilyPlacement {
  members: string[]
  memberSet: Set<string>
  parentOf: Map<string, Set<string>>
  partnerOf: Map<string, Set<string>>
  familyLeft: number
  positions: Positions
  byId: Map<string, GraphCharacter>
  boxes: LayoutBox[]
}

export interface FamilyRow {
  g: number
  generation: Map<string, number>
  placedAtGen: Map<number, string[]>
  coupleToKids: Map<string, CoupleChildren>
}
