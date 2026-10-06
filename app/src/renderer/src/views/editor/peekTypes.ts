import { type PeekAnchor } from './peekPlacement'

// Shapes returned by the backend `entities/peek` method (camelCase over RPC).
interface PeekImage {
  name: string
  url: string
}

export interface PeekPill {
  text: string | null
  labelKey: string | null
  arg: string | null
  dim: boolean
  color: string
  icon: string | null
}

interface PeekProp {
  key: string
  value: string
}

interface PeekRelTarget {
  name: string
  entityId: string | null
  typeKey: string | null
}

interface PeekRel {
  role: string
  targets: PeekRelTarget[]
}

interface PeekSection {
  title: string
  content: string
}

interface PeekMapPin {
  mapId: string
  mapName: string
  pinId: string
  pinLabel: string
}

export interface EntityPeek {
  id: string
  typeKey: string
  title: string
  customTypeLabel: string | null
  badgeColor: string
  description: string
  images: PeekImage[]
  pills: PeekPill[]
  appearanceProps: PeekProp[]
  customProps: PeekProp[]
  relationships: PeekRel[]
  sections: PeekSection[]
  mapPins: PeekMapPin[]
  scopeLabel: string | null
  /** Cached findings about this entity from a previous chapter analysis. Absent
   *  when none has been run for the open chapter. */
  aiFindings: PeekFinding[] | null
}

interface PeekFinding {
  type: string
  title: string
  description: string
  excerpt: string
}

/** The open editor's chapter/scene, threaded into `entities/peek` so a character
 * peek resolves its per-chapter/scene overrides for the scope currently in view. */
export interface PeekScope {
  chapterGuid: string | null
  chapterTitle: string | null
  sceneTitle: string | null
  /** The open scene, so the card can silence this entry's detection here. */
  sceneId: string | null
}

/** Where the peek card sits, plus which entity it currently shows. */
export interface HoverCard {
  /** What the card belongs to, in viewport coordinates: the whole word, not the
   *  pixel under the pointer. Keeping the anchor stable while the pointer moves
   *  inside the word is what stops the card jumping about. */
  anchor: PeekAnchor
  prefer: 'below' | 'beside'
  entityType: string
  entityId: string
}

/** Imperative controls plus the rendered overlay for a shared entity peek. Both
 * the editor (driven by iframe hover messages) and the context sidebar (driven by
 * card mouse-enter/leave) use one of these so the peek behaves identically. */
export interface EntityPeekController {
  /** Show the peek for an entity, anchored to a rectangle in viewport
   * coordinates - the hovered word, or the sidebar row it belongs to. Showing
   * the same entity against the same rectangle again is a no-op, so a pointer
   * moving inside the word neither moves the card nor refetches it. A pinned
   * card ignores this and stays put, matching the desktop app. */
  showAt(
    target: { entityType: string; entityId: string },
    anchor: PeekAnchor,
    prefer?: 'below' | 'beside'
  ): void
  /** Debounced hide — cancelled if the pointer reaches the card (or it is pinned),
   * so moving onto the card never dismisses it. */
  scheduleHide(): void
  /** Cancel a pending hide (pointer re-entered the trigger or the card). */
  clearHide(): void
  /** Hide immediately unless the card is pinned (e.g. a click in the editor). */
  hide(): void
  /** True while the pointer is over the card itself — lets callers guard against a
   * late async exit event closing the card the moment the pointer reaches it. */
  isPointerOverCard(): boolean
  /** The anchored PeekCard overlay to render (or null when nothing is shown). */
  overlay: React.JSX.Element | null
}
