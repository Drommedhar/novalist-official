import { type ConnectorSide, type BoardPoint, type CanvasCard, type CanvasConnector, CONNECTOR_SIDES } from './canvasTypes'

export function isConnectorSide(value: string | undefined): value is ConnectorSide {
  return value === 'top' || value === 'right' || value === 'bottom' || value === 'left'
}

function cardCentre(card: CanvasCard): BoardPoint {
  return { x: card.x + card.width / 2, y: card.y + card.height / 2 }
}

export function sidesFacingEachOther(
  from: CanvasCard,
  to: CanvasCard
): { from: ConnectorSide; to: ConnectorSide } {
  const a = cardCentre(from)
  const b = cardCentre(to)
  const dx = b.x - a.x
  const dy = b.y - a.y
  if (Math.abs(dx) >= Math.abs(dy)) {
    return dx >= 0 ? { from: 'right', to: 'left' } : { from: 'left', to: 'right' }
  }
  return dy >= 0 ? { from: 'bottom', to: 'top' } : { from: 'top', to: 'bottom' }
}

export function pointOnCard(card: CanvasCard, side: ConnectorSide): BoardPoint {
  switch (side) {
    case 'top':
      return { x: card.x + card.width / 2, y: card.y }
    case 'right':
      return { x: card.x + card.width, y: card.y + card.height / 2 }
    case 'bottom':
      return { x: card.x + card.width / 2, y: card.y + card.height }
    case 'left':
      return { x: card.x, y: card.y + card.height / 2 }
  }
}

export function nearestCardSide(
  card: CanvasCard,
  point: BoardPoint,
  tieBreaker: ConnectorSide
): ConnectorSide {
  const distance: Record<ConnectorSide, number> = {
    top: Math.abs(point.y - card.y),
    right: Math.abs(point.x - (card.x + card.width)),
    bottom: Math.abs(point.y - (card.y + card.height)),
    left: Math.abs(point.x - card.x)
  }
  let nearest = tieBreaker
  for (const side of CONNECTOR_SIDES) {
    if (distance[side] < distance[nearest]) nearest = side
  }
  return nearest
}

export function connectorPoints(
  connector: CanvasConnector,
  from: CanvasCard,
  to: CanvasCard
): { start: BoardPoint; end: BoardPoint } {
  const fallback = sidesFacingEachOther(from, to)
  const fromSide = isConnectorSide(connector.fromSide) ? connector.fromSide : fallback.from
  const toSide = isConnectorSide(connector.toSide) ? connector.toSide : fallback.to
  return { start: pointOnCard(from, fromSide), end: pointOnCard(to, toSide) }
}

/**
 * Centre an overlay on its connector until doing so would put part of the
 * control outside the board's non-negative scroll plane. CSS percentages in a
 * transform resolve against the overlay itself, so this clamps any label
 * width without duplicating design-token dimensions in TypeScript.
 */
export function connectorOverlayStyle(midpoint: BoardPoint): React.CSSProperties {
  return {
    left: midpoint.x,
    top: midpoint.y,
    transform: `translate(max(-50%, ${-midpoint.x}px), max(-50%, ${-midpoint.y}px))`
  }
}
