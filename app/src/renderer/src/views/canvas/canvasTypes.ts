

export type ConnectorSide = 'top' | 'right' | 'bottom' | 'left'

export interface BoardPoint {
  x: number
  y: number
}

export interface CanvasCard {
  id: string
  title: string
  text: string
  x: number
  y: number
  width: number
  height: number
  color: string
  sceneId: string
  chapterGuid: string
  entityId: string
}

export interface CanvasConnector {
  id: string
  fromCardId: string
  toCardId: string
  label: string
  /** Empty on boards written before edge handles existed. */
  fromSide: string
  /** Empty on boards written before edge handles existed. */
  toSide: string
}

export interface Canvas {
  id: string
  name: string
  panX: number
  panY: number
  zoom: number
  cards: CanvasCard[]
  connectors: CanvasConnector[]
}

export interface CanvasSummary {
  id: string
  name: string
}

export interface CardDragGesture {
  pointerId: number
  cardId: string
  offsetX: number
  offsetY: number
  startClientX: number
  startClientY: number
  moved: boolean
  capture: HTMLButtonElement
}

export interface ConnectorDragGesture {
  pointerId: number
  fromCardId: string
  fromSide: ConnectorSide
  capture: HTMLButtonElement
}

export interface ConnectorPreview {
  fromCardId: string
  fromSide: ConnectorSide
  end: BoardPoint
  targetCardId: string | null
  targetSide: ConnectorSide | null
}

export interface ConnectorDropTarget {
  card: CanvasCard
  side: ConnectorSide
}

export const CONNECTOR_SIDES: ConnectorSide[] = ['top', 'right', 'bottom', 'left']

/** Autosave delay, matching the editor's. */
export const SAVE_DELAY_MS = 2000

/** Avoid turning a slightly unsteady click on the grip into a move. */
export const DRAG_THRESHOLD_PX = 4

/** One keyboard nudge follows the renderer's medium spacing step. */
export const CARD_NUDGE_PX = 12

/** Half the phone-sized handle: switch inward before a touch target can clip. */
export const HANDLE_EDGE_GUARD_PX = 22
