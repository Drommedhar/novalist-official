import { type MainView, type PaneNode, type SavedLayout } from './shellTypes'

let paneSeq = 0

function paneId(): string {
  paneSeq += 1
  return `pane-${paneSeq}`
}

export function newLeaf(view: MainView): PaneNode {
  return { kind: 'leaf', id: paneId(), view }
}

/**
 * Whether two pane trees are the same arrangement.
 *
 * Ids are deliberately ignored: applying a layout re-identifies every node, so
 * a tree restored from a layout never shares an id with the one that was saved.
 * What makes two arrangements the same is their shape, their direction, their
 * proportions and the view in each leaf.
 */
export function sameArrangement(a: PaneNode, b: PaneNode): boolean {
  if (a.kind !== b.kind) return false
  if (a.kind === 'leaf') return a.view === (b as typeof a).view

  const other = b as typeof a
  return (
    a.direction === other.direction &&
    a.children.length === other.children.length &&
    // A dragged divider is a different arrangement, but a pixel of rounding is
    // not - the sizes are floats the writer never typed.
    a.sizes.every((size, i) => Math.abs(size - (other.sizes[i] ?? 0)) < 0.5) &&
    a.children.every((child, i) => sameArrangement(child, other.children[i]))
  )
}

/**
 * The built-in single-pane layout, always offered and never deletable.
 *
 * Not a `SavedLayout`: it is the arrangement the window starts in rather than
 * one the writer named, so keeping it out of the stored list is what stops it
 * being renamed, overwritten or forgotten. The sentinel cannot collide with a
 * layout somebody names "Default" either.
 */
export const DEFAULT_LAYOUT = '__default'

/**
 * The layout the window is currently in, or '' when it is in none.
 *
 * A layout the writer named wins over the built-in default: both match an
 * unsplit window, and the name they chose says more than "Default" does.
 */
export function matchingLayout(panes: PaneNode, layouts: SavedLayout[]): string {
  const named = layouts.find((l) => sameArrangement(panes, l.root))
  if (named) return named.name
  return panes.kind === 'leaf' ? DEFAULT_LAYOUT : ''
}

/** The leaf with this id, or null. */
export function findPane(node: PaneNode, id: string): PaneNode | null {
  if (node.id === id) return node
  if (node.kind === 'split') {
    for (const child of node.children) {
      const found = findPane(child, id)
      if (found) return found
    }
  }
  return null
}

/** Every leaf, left to right, top to bottom. */
export function paneLeaves(node: PaneNode): Extract<PaneNode, { kind: 'leaf' }>[] {
  return node.kind === 'leaf' ? [node] : node.children.flatMap(paneLeaves)
}

/**
 * Whether any pane is showing one of these views.
 *
 * What the surfaces around the content area have to ask. `mainView` names the
 * view of the pane the writer is in, so a shell asking it about the editor got
 * "no" the moment they clicked into the Codex beside it - which is how the
 * context sidebar and the notes dock disappeared as soon as anyone split the
 * window.
 */
export function anyPaneShows(node: PaneNode, views: MainView[]): boolean {
  return paneLeaves(node).some((leaf) => views.includes(leaf.view))
}

/**
 * Splits a leaf in two. The new pane opens on the same view, because splitting
 * to look at the same thing twice - two places in one manuscript - is at least
 * as common as splitting to look at two different things.
 */
export function splitPane(
  node: PaneNode,
  id: string,
  direction: 'row' | 'column'
): { root: PaneNode; created: string | null } {
  if (node.kind === 'leaf') {
    if (node.id !== id) return { root: node, created: null }
    const fresh = newLeaf(node.view)
    return {
      root: {
        kind: 'split',
        id: paneId(),
        direction,
        children: [node, fresh],
        sizes: [50, 50]
      },
      created: fresh.id
    }
  }

  let created: string | null = null
  const children = node.children.map((child) => {
    const result = splitPane(child, id, direction)
    if (result.created) created = result.created
    return result.root
  })
  return { root: { ...node, children }, created }
}

/**
 * Removes a pane. A split left holding one child collapses into it, or the
 * tree grows a spine of pointless containers as panes come and go.
 */
export function closePane(node: PaneNode, id: string): PaneNode | null {
  if (node.id === id) return null
  if (node.kind === 'leaf') return node

  const children = node.children
    .map((child) => closePane(child, id))
    .filter((child): child is PaneNode => child !== null)

  if (children.length === 0) return null
  if (children.length === 1) return children[0]
  return { ...node, children, sizes: even(children.length) }
}

/** Points one leaf at a different view. */
export function setPaneViewIn(node: PaneNode, id: string, view: MainView): PaneNode {
  if (node.kind === 'leaf') return node.id === id ? { ...node, view } : node
  return { ...node, children: node.children.map((c) => setPaneViewIn(c, id, view)) }
}

function even(count: number): number[] {
  return Array.from({ length: count }, () => 100 / count)
}

/** Sets one split's proportions. */
export function resize(node: PaneNode, splitId: string, sizes: number[]): PaneNode {
  if (node.kind === 'leaf') return node
  if (node.id === splitId) return { ...node, sizes }
  return { ...node, children: node.children.map((c) => resize(c, splitId, sizes)) }
}

/* Layouts are about the writer's screen rather than their book, so they live
 * beside the other view-state preferences instead of in the project.
 *
 * Its own key: pane layouts and the named workspace layouts (layoutStore) both
 * called themselves "nl.shell.layouts" and stored different shapes under it, so
 * saving one kind erased the other and opening the pane-layout list on an entry
 * the dialog had written asked for the leaves of a tree that was not there. */
const LAYOUT_STORAGE_KEY = 'nl.shell.paneLayouts'

function readLayouts(): SavedLayout[] {
  try {
    const raw = localStorage.getItem(LAYOUT_STORAGE_KEY)
    const parsed: unknown = raw ? JSON.parse(raw) : []
    if (!Array.isArray(parsed)) return []
    // Anything without a pane tree is not a pane layout, whoever wrote it.
    return (parsed as SavedLayout[]).filter(
      (layout) => typeof layout?.name === 'string' && isPaneNode(layout.root)
    )
  } catch {
    return []
  }
}

function isPaneNode(node: unknown): node is PaneNode {
  if (!node || typeof node !== 'object') return false
  const candidate = node as PaneNode
  if (candidate.kind === 'leaf') return typeof candidate.id === 'string'
  return (
    candidate.kind === 'split' &&
    Array.isArray(candidate.children) &&
    candidate.children.every(isPaneNode)
  )
}

/**
 * Gives a restored tree fresh pane ids.
 *
 * Ids are handed out per session, so a layout saved yesterday holds "pane-2"
 * while this session is about to hand that name to the next split - and two
 * panes answering to one id are one pane as far as everything keyed by it is
 * concerned, which for the editor means both showing the same scene.
 */
export function reidentify(node: PaneNode): PaneNode {
  return node.kind === 'leaf'
    ? { ...node, id: paneId() }
    : { ...node, id: paneId(), children: node.children.map(reidentify) }
}

export function persistLayouts(layouts: SavedLayout[]): void {
  try {
    localStorage.setItem(LAYOUT_STORAGE_KEY, JSON.stringify(layouts))
  } catch {
    // A full or blocked store costs the writer their saved layouts, not their
    // session.
  }
}

export const storedLayouts = readLayouts()

export const initialPanes = newLeaf('write')
