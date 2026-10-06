import { type ConnectorSide, type BoardPoint, type CanvasCard, type ConnectorDropTarget, DRAG_THRESHOLD_PX } from './canvasTypes'
import { isConnectorSide, sidesFacingEachOther, pointOnCard, nearestCardSide } from './canvasGeometry'
import type { useCanvasState, useCanvasConnectorCommit } from './canvasState'
import type { createCanvasCards } from './canvasActions'
import type { createCanvasConnection } from './canvasConnections'

export function createCanvasCoordinates(context: CanvasCoordinatesContext) {
  const {
    surfaceRef, chapters, pendingTitles, dragPosition, canvas, canvasRef
  } = context
  const toBoardPoint = (clientX: number, clientY: number): BoardPoint => {
    const surface = surfaceRef.current
    if (!surface) return { x: clientX, y: clientY }
    const bounds = surface.getBoundingClientRect()
    return {
      x: clientX - bounds.left + surface.scrollLeft,
      y: clientY - bounds.top + surface.scrollTop
    }
  }

  const renderedCard = (card: CanvasCard): CanvasCard => {
    const chapter = chapters.find((c) => c.scenes.some((s) => s.id === card.sceneId))
    const scene = chapter?.scenes.find((s) => s.id === card.sceneId)
    return {
      ...card,
      title: pendingTitles.current.has(card.id) ? card.title : (scene?.title ?? card.title),
      sceneId: scene?.id ?? '',
      chapterGuid: chapter?.guid ?? '',
      ...(dragPosition?.cardId === card.id ? { x: dragPosition.x, y: dragPosition.y } : {})
    }
  }

  const cardById = (id: string): CanvasCard | undefined => {
    const card = canvas?.cards.find((item) => item.id === id)
    return card ? renderedCard(card) : undefined
  }

  const findDropTarget = (
    clientX: number,
    clientY: number,
    fromCardId: string
  ): ConnectorDropTarget | null => {
    const current = canvasRef.current
    if (!current) return null
    // Only the topmost element may receive a drop. Walking through every layer
    // would let a visible source card (or another overlay) connect to a card
    // hidden underneath it.
    const element = document.elementFromPoint(clientX, clientY)
    const cardElement = element?.closest<HTMLElement>('[data-canvas-card-id]')
    const cardId = cardElement?.dataset.canvasCardId
    if (!cardId || cardId === fromCardId) return null
    const card = current.cards.find((item) => item.id === cardId)
    if (!card) return null
    const handleElement = element?.closest<HTMLElement>('[data-connector-side]')
    const explicitSide = handleElement?.dataset.connectorSide
    if (isConnectorSide(explicitSide)) return { card, side: explicitSide }
    const from = current.cards.find((item) => item.id === fromCardId)
    const tieBreaker = from ? sidesFacingEachOther(from, card).to : 'left'
    return {
      card,
      side: nearestCardSide(card, toBoardPoint(clientX, clientY), tieBreaker)
    }
  }
  return { toBoardPoint, renderedCard, cardById, findDropTarget }
}

export function createCanvasCardDragging(context: CanvasCardDraggingContext) {
  const {
    dragPositionRef, dragFrame, setDragPosition, commitActiveConnectorEdit, toBoardPoint,
    cardDragRef, setSelectedId, setSelectedConnectorId, setKeyboardConnectFrom, updateCard
  } = context
  const scheduleDragPosition = (next: BoardPoint & { cardId: string }): void => {
    dragPositionRef.current = next
    if (dragFrame.current) return
    dragFrame.current = window.requestAnimationFrame(() => {
      dragFrame.current = null
      setDragPosition(dragPositionRef.current)
    })
  }

  const startCardDrag = (event: React.PointerEvent<HTMLButtonElement>, card: CanvasCard): void => {
    if (event.button !== 0 || !event.isPrimary) return
    event.preventDefault()
    event.stopPropagation()
    commitActiveConnectorEdit()
    const point = toBoardPoint(event.clientX, event.clientY)
    event.currentTarget.setPointerCapture(event.pointerId)
    cardDragRef.current = {
      pointerId: event.pointerId,
      cardId: card.id,
      offsetX: point.x - card.x,
      offsetY: point.y - card.y,
      startClientX: event.clientX,
      startClientY: event.clientY,
      moved: false,
      capture: event.currentTarget
    }
    dragPositionRef.current = { cardId: card.id, x: card.x, y: card.y }
    setSelectedId(card.id)
    setSelectedConnectorId(null)
    setKeyboardConnectFrom(null)
  }

  const moveCardDrag = (event: React.PointerEvent<HTMLButtonElement>): void => {
    const gesture = cardDragRef.current
    if (!gesture || gesture.pointerId !== event.pointerId) return
    if (
      !gesture.moved &&
      Math.hypot(event.clientX - gesture.startClientX, event.clientY - gesture.startClientY) <
        DRAG_THRESHOLD_PX
    ) {
      return
    }
    gesture.moved = true
    const point = toBoardPoint(event.clientX, event.clientY)
    scheduleDragPosition({
      cardId: gesture.cardId,
      x: Math.max(0, point.x - gesture.offsetX),
      y: Math.max(0, point.y - gesture.offsetY)
    })
  }

  const finishCardDrag = (event: React.PointerEvent<HTMLButtonElement>): void => {
    const gesture = cardDragRef.current
    if (!gesture || gesture.pointerId !== event.pointerId) return
    if (gesture.moved) {
      const point = toBoardPoint(event.clientX, event.clientY)
      const finalPosition = {
        x: Math.max(0, point.x - gesture.offsetX),
        y: Math.max(0, point.y - gesture.offsetY)
      }
      if (dragFrame.current) window.cancelAnimationFrame(dragFrame.current)
      dragFrame.current = null
      dragPositionRef.current = null
      setDragPosition(null)
      updateCard(gesture.cardId, finalPosition)
    }
    cardDragRef.current = null
    if (gesture.capture.hasPointerCapture(event.pointerId)) {
      gesture.capture.releasePointerCapture(event.pointerId)
    }
  }

  const cancelCardDrag = (pointerId: number): void => {
    if (cardDragRef.current?.pointerId !== pointerId) return
    if (dragFrame.current) window.cancelAnimationFrame(dragFrame.current)
    dragFrame.current = null
    dragPositionRef.current = null
    cardDragRef.current = null
    setDragPosition(null)
  }
  return { scheduleDragPosition, startCardDrag, moveCardDrag, finishCardDrag, cancelCardDrag }
}

export function createCanvasConnectorDragging(context: CanvasConnectorDraggingContext) {
  const {
    commitActiveConnectorEdit, connectorDragRef, setConnectorPreview, setKeyboardConnectFrom,
    setSelectedId, setSelectedConnectorId, findDropTarget, toBoardPoint, createConnector
  } = context
  const startConnectorDrag = (
    event: React.PointerEvent<HTMLButtonElement>,
    card: CanvasCard,
    side: ConnectorSide
  ): void => {
    if (event.button !== 0 || !event.isPrimary) return
    event.preventDefault()
    event.stopPropagation()
    commitActiveConnectorEdit()
    event.currentTarget.setPointerCapture(event.pointerId)
    connectorDragRef.current = {
      pointerId: event.pointerId,
      fromCardId: card.id,
      fromSide: side,
      capture: event.currentTarget
    }
    const start = pointOnCard(card, side)
    setConnectorPreview({
      fromCardId: card.id,
      fromSide: side,
      end: start,
      targetCardId: null,
      targetSide: null
    })
    setKeyboardConnectFrom(null)
    setSelectedId(card.id)
    setSelectedConnectorId(null)
  }

  const moveConnectorDrag = (event: React.PointerEvent<HTMLButtonElement>): void => {
    const gesture = connectorDragRef.current
    if (!gesture || gesture.pointerId !== event.pointerId) return
    const target = findDropTarget(event.clientX, event.clientY, gesture.fromCardId)
    setConnectorPreview({
      fromCardId: gesture.fromCardId,
      fromSide: gesture.fromSide,
      end: target
        ? pointOnCard(target.card, target.side)
        : toBoardPoint(event.clientX, event.clientY),
      targetCardId: target?.card.id ?? null,
      targetSide: target?.side ?? null
    })
  }

  const finishConnectorDrag = (event: React.PointerEvent<HTMLButtonElement>): void => {
    const gesture = connectorDragRef.current
    if (!gesture || gesture.pointerId !== event.pointerId) return
    const target = findDropTarget(event.clientX, event.clientY, gesture.fromCardId)
    connectorDragRef.current = null
    setConnectorPreview(null)
    if (gesture.capture.hasPointerCapture(event.pointerId)) {
      gesture.capture.releasePointerCapture(event.pointerId)
    }
    if (target) {
      createConnector(gesture.fromCardId, target.card.id, gesture.fromSide, target.side)
    }
  }

  const cancelConnectorDrag = (pointerId: number): void => {
    if (connectorDragRef.current?.pointerId !== pointerId) return
    connectorDragRef.current = null
    setConnectorPreview(null)
  }
  return { startConnectorDrag, moveConnectorDrag, finishConnectorDrag, cancelConnectorDrag }
}

type CanvasCoordinatesContext = Pick<ReturnType<typeof useCanvasState>, 'surfaceRef' | 'chapters' | 'pendingTitles' | 'dragPosition' | 'canvas' | 'canvasRef'>

type CanvasCardDraggingContext = Pick<ReturnType<typeof useCanvasState>, 'dragPositionRef' | 'dragFrame' | 'setDragPosition' | 'cardDragRef' | 'setSelectedId' | 'setSelectedConnectorId' | 'setKeyboardConnectFrom'> &
  Pick<ReturnType<typeof useCanvasConnectorCommit>, 'commitActiveConnectorEdit'> &
  Pick<ReturnType<typeof createCanvasCoordinates>, 'toBoardPoint'> &
  Pick<ReturnType<typeof createCanvasCards>, 'updateCard'>

type CanvasConnectorDraggingContext = Pick<ReturnType<typeof useCanvasConnectorCommit>, 'commitActiveConnectorEdit'> &
  Pick<ReturnType<typeof useCanvasState>, 'connectorDragRef' | 'setConnectorPreview' | 'setKeyboardConnectFrom' | 'setSelectedId' | 'setSelectedConnectorId'> &
  Pick<ReturnType<typeof createCanvasCoordinates>, 'findDropTarget' | 'toBoardPoint'> &
  Pick<ReturnType<typeof createCanvasConnection>, 'createConnector'>
