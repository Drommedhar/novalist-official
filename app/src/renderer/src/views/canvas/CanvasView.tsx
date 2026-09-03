import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { FileUp, GripHorizontal, Pencil, Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { InputDialog } from '../../shell/InputDialog'
import { useProjectStore, type ProjectStateDto } from '../../stores/projectStore'
import { persistPendingWrite, registerPendingWrite } from '../../stores/pendingWrites'
import './canvas.css'

type ConnectorSide = 'top' | 'right' | 'bottom' | 'left'

interface BoardPoint {
  x: number
  y: number
}

interface CanvasCard {
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

interface CanvasConnector {
  id: string
  fromCardId: string
  toCardId: string
  label: string
  /** Empty on boards written before edge handles existed. */
  fromSide: string
  /** Empty on boards written before edge handles existed. */
  toSide: string
}

interface Canvas {
  id: string
  name: string
  panX: number
  panY: number
  zoom: number
  cards: CanvasCard[]
  connectors: CanvasConnector[]
}

interface CanvasSummary {
  id: string
  name: string
}

interface CardDragGesture {
  pointerId: number
  cardId: string
  offsetX: number
  offsetY: number
  startClientX: number
  startClientY: number
  moved: boolean
  capture: HTMLButtonElement
}

interface ConnectorDragGesture {
  pointerId: number
  fromCardId: string
  fromSide: ConnectorSide
  capture: HTMLButtonElement
}

interface ConnectorPreview {
  fromCardId: string
  fromSide: ConnectorSide
  end: BoardPoint
  targetCardId: string | null
  targetSide: ConnectorSide | null
}

interface ConnectorDropTarget {
  card: CanvasCard
  side: ConnectorSide
}

const CONNECTOR_SIDES: ConnectorSide[] = ['top', 'right', 'bottom', 'left']
/** Autosave delay, matching the editor's. */
const SAVE_DELAY_MS = 2000
/** Avoid turning a slightly unsteady click on the grip into a move. */
const DRAG_THRESHOLD_PX = 4
/** One keyboard nudge follows the renderer's medium spacing step. */
const CARD_NUDGE_PX = 12
/** Half the phone-sized handle: switch inward before a touch target can clip. */
const HANDLE_EDGE_GUARD_PX = 22

function isConnectorSide(value: string | undefined): value is ConnectorSide {
  return value === 'top' || value === 'right' || value === 'bottom' || value === 'left'
}

function cardCentre(card: CanvasCard): BoardPoint {
  return { x: card.x + card.width / 2, y: card.y + card.height / 2 }
}

function sidesFacingEachOther(
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

function pointOnCard(card: CanvasCard, side: ConnectorSide): BoardPoint {
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

function nearestCardSide(
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

function connectorPoints(
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
function connectorOverlayStyle(midpoint: BoardPoint): React.CSSProperties {
  return {
    left: midpoint.x,
    top: midpoint.y,
    transform: `translate(max(-50%, ${-midpoint.x}px), max(-50%, ${-midpoint.y}px))`
  }
}

/**
 * Freeform planning board: loose cards and author-drawn labelled connectors on
 * an infinite surface.
 *
 * Nothing here is part of the manuscript. A card only becomes a scene when the
 * writer promotes it, which is what keeps the board usable for half-formed
 * ideas that should not yet count towards a word target.
 */
export function CanvasView(): React.JSX.Element {
  const { t } = useTranslation()
  const chapters = useProjectStore((s) => s.chapters)

  const [boards, setBoards] = useState<CanvasSummary[]>([])
  const [canvas, setCanvas] = useState<Canvas | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [selectedConnectorId, setSelectedConnectorId] = useState<string | null>(null)
  const [keyboardConnectFrom, setKeyboardConnectFrom] = useState<{
    cardId: string
    side: ConnectorSide
  } | null>(null)
  const [connectorPreview, setConnectorPreview] = useState<ConnectorPreview | null>(null)
  const [dragPosition, setDragPosition] = useState<(BoardPoint & { cardId: string }) | null>(null)
  /** Which name the writer is being asked for, if any. */
  const [naming, setNaming] = useState<'create' | 'rename' | null>(null)

  const surfaceRef = useRef<HTMLDivElement | null>(null)
  const canvasRef = useRef<Canvas | null>(null)
  const cardDragRef = useRef<CardDragGesture | null>(null)
  const connectorDragRef = useRef<ConnectorDragGesture | null>(null)
  const dragPositionRef = useRef<(BoardPoint & { cardId: string }) | null>(null)
  const dragFrame = useRef<number | null>(null)
  const saveTimer = useRef<number | null>(null)
  const pendingSave = useRef<Canvas | null>(null)
  const inFlightSave = useRef<Promise<unknown> | null>(null)
  const connectorInputRefs = useRef(new Map<string, HTMLInputElement>())
  const connectorLabelRefs = useRef(new Map<string, HTMLButtonElement>())
  const cardMoveHandleRefs = useRef(new Map<string, HTMLButtonElement>())
  const editingOriginalLabel = useRef<{ id: string; label: string } | null>(null)

  const replaceCanvas = useCallback((next: Canvas | null): void => {
    canvasRef.current = next
    setCanvas(next)
  }, [])

  const loadBoards = useCallback(async () => {
    const list = await rpc.request<CanvasSummary[]>('canvas/list')
    setBoards(list)
    if (list.length > 0 && !canvasRef.current) {
      replaceCanvas(await rpc.request<Canvas | null>('canvas/load', [list[0].id]))
    }
  }, [replaceCanvas])

  useEffect(() => {
    void loadBoards()
  }, [loadBoards])

  /** Writes and clears the latest debounced board change, if there is one. */
  const flushPendingSave = useCallback(async (): Promise<void> => {
    if (saveTimer.current) window.clearTimeout(saveTimer.current)
    saveTimer.current = null
    while (true) {
      const active = inFlightSave.current
      if (active) {
        await active
        continue
      }
      const pending = pendingSave.current
      if (!pending) return
      const request = persistPendingWrite(`canvas:${pending.id}`, () =>
        rpc.request('canvas/save', [pending])
      )
      inFlightSave.current = request
      try {
        await request
        if (pendingSave.current === pending) pendingSave.current = null
      } finally {
        if (inFlightSave.current === request) inFlightSave.current = null
      }
    }
  }, [])

  /** Queues a save. Every persistent mutation goes through here. */
  const queueSave = useCallback(
    (next: Canvas) => {
      replaceCanvas(next)
      if (saveTimer.current) window.clearTimeout(saveTimer.current)
      pendingSave.current = next
      saveTimer.current = window.setTimeout(() => {
        void flushPendingSave()
      }, SAVE_DELAY_MS)
    },
    [flushPendingSave, replaceCanvas]
  )

  /** Commit the live inline value before another control unmounts its editor. */
  const commitActiveConnectorEdit = useCallback((restore = false): void => {
    const editing = editingOriginalLabel.current
    const current = canvasRef.current
    if (!editing || !current) return
    const connector = current.connectors.find((item) => item.id === editing.id)
    if (connector) {
      const label = restore ? editing.label : connector.label.trim()
      if (label !== connector.label) {
        queueSave({
          ...current,
          connectors: current.connectors.map((item) =>
            item.id === editing.id ? { ...item, label } : item
          )
        })
      }
    }
    editingOriginalLabel.current = null
  }, [queueSave])

  useEffect(
    () =>
      registerPendingWrite(async () => {
        commitActiveConnectorEdit()
        await flushPendingSave()
      }),
    [commitActiveConnectorEdit, flushPendingSave]
  )

  // Flush persistent work when leaving, while discarding purely visual pointer
  // gestures. A cancelled drag must never become project data.
  useEffect(
    () => () => {
      if (dragFrame.current) window.cancelAnimationFrame(dragFrame.current)
      cardDragRef.current = null
      connectorDragRef.current = null
      // flushPendingSave registers the actual board payload globally before it
      // awaits the backend, so the acknowledgement survives this component.
      void flushPendingSave().catch(() => {})
    },
    [flushPendingSave]
  )

  useEffect(() => {
    if (!selectedConnectorId) return
    const frame = window.requestAnimationFrame(() => {
      const input = connectorInputRefs.current.get(selectedConnectorId)
      input?.focus()
      input?.select()
    })
    return () => window.cancelAnimationFrame(frame)
  }, [selectedConnectorId])

  useEffect(() => {
    if (!keyboardConnectFrom) return
    const cancel = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      setKeyboardConnectFrom(null)
    }
    window.addEventListener('keydown', cancel)
    return () => window.removeEventListener('keydown', cancel)
  }, [keyboardConnectFrom])

  const clearTransientGestures = (): void => {
    if (dragFrame.current) window.cancelAnimationFrame(dragFrame.current)
    dragFrame.current = null
    dragPositionRef.current = null
    cardDragRef.current = null
    connectorDragRef.current = null
    setDragPosition(null)
    setConnectorPreview(null)
    setKeyboardConnectFrom(null)
  }

  const clearSelection = (commitConnector = true): void => {
    if (commitConnector) commitActiveConnectorEdit()
    else editingOriginalLabel.current = null
    setSelectedId(null)
    setSelectedConnectorId(null)
  }

  const createBoard = async (name: string): Promise<void> => {
    commitActiveConnectorEdit()
    await flushPendingSave()
    const created = await rpc.request<Canvas>('canvas/create', [name])
    clearTransientGestures()
    clearSelection()
    replaceCanvas(created)
    await loadBoards()
  }

  /** Rename writes immediately because the picker displays the new value. */
  const renameBoard = async (name: string): Promise<void> => {
    commitActiveConnectorEdit()
    const current = canvasRef.current
    if (!current) return
    await flushPendingSave()
    const renamed = { ...current, name }
    replaceCanvas(renamed)
    await persistPendingWrite(`canvas:${renamed.id}`, () => rpc.request('canvas/save', [renamed]))
    setBoards(await rpc.request<CanvasSummary[]>('canvas/list'))
  }

  const switchBoard = async (id: string): Promise<void> => {
    commitActiveConnectorEdit()
    await flushPendingSave()
    clearTransientGestures()
    clearSelection(false)
    replaceCanvas(await rpc.request<Canvas | null>('canvas/load', [id]))
  }

  // Deleting the open board drops any queued save with it, otherwise the
  // debounce would rewrite the file we just removed.
  const deleteBoard = async (): Promise<void> => {
    const current = canvasRef.current
    if (!current) return
    if (!window.confirm(t('canvas.deleteBoardConfirm', { name: current.name }))) return
    if (saveTimer.current) window.clearTimeout(saveTimer.current)
    saveTimer.current = null
    pendingSave.current = null
    clearTransientGestures()
    clearSelection(false)
    await rpc.request<boolean>('canvas/delete', [current.id])
    const list = await rpc.request<CanvasSummary[]>('canvas/list')
    setBoards(list)
    replaceCanvas(
      list.length > 0 ? await rpc.request<Canvas | null>('canvas/load', [list[0].id]) : null
    )
  }

  const addCard = (): void => {
    commitActiveConnectorEdit()
    const current = canvasRef.current
    if (!current) return
    const card: CanvasCard = {
      id: `card-${Date.now()}`,
      title: '',
      text: '',
      // Dropped near the viewport origin rather than at (0,0), so a new card is
      // visible without hunting for it.
      x: 80 + current.cards.length * 24,
      y: 80 + current.cards.length * 16,
      width: 200,
      height: 120,
      color: '',
      sceneId: '',
      chapterGuid: '',
      entityId: ''
    }
    queueSave({ ...current, cards: [...current.cards, card] })
    setSelectedId(card.id)
    setSelectedConnectorId(null)
  }

  const updateCard = (id: string, patch: Partial<CanvasCard>): void => {
    const current = canvasRef.current
    if (!current) return
    queueSave({
      ...current,
      cards: current.cards.map((card) => (card.id === id ? { ...card, ...patch } : card))
    })
  }

  const deleteCard = (id: string): void => {
    const current = canvasRef.current
    if (!current) return
    queueSave({
      ...current,
      cards: current.cards.filter((card) => card.id !== id),
      // A connector to a card that no longer exists would draw to nowhere.
      connectors: current.connectors.filter(
        (connector) => connector.fromCardId !== id && connector.toCardId !== id
      )
    })
    setKeyboardConnectFrom(null)
    setConnectorPreview(null)
    clearSelection()
  }

  const beginConnectorEdit = (id: string): void => {
    const connector = canvasRef.current?.connectors.find((item) => item.id === id)
    if (!connector) return
    if (editingOriginalLabel.current && editingOriginalLabel.current.id !== id) {
      commitActiveConnectorEdit()
    }
    if (editingOriginalLabel.current?.id !== id) {
      editingOriginalLabel.current = { id, label: connector.label }
    }
    setSelectedId(null)
    setKeyboardConnectFrom(null)
    setSelectedConnectorId(id)
    window.requestAnimationFrame(() => {
      const input = connectorInputRefs.current.get(id)
      input?.focus()
      input?.select()
    })
  }

  const updateConnector = (id: string, patch: Partial<CanvasConnector>): void => {
    const current = canvasRef.current
    if (!current) return
    queueSave({
      ...current,
      connectors: current.connectors.map((connector) =>
        connector.id === id ? { ...connector, ...patch } : connector
      )
    })
  }

  const closeConnectorEdit = (
    id: string,
    options: { restore: boolean; returnFocus: boolean }
  ): void => {
    const connector = canvasRef.current?.connectors.find((item) => item.id === id)
    if (connector) {
      const original = editingOriginalLabel.current
      const label = options.restore && original?.id === id ? original.label : connector.label.trim()
      if (label !== connector.label) updateConnector(id, { label })
    }
    editingOriginalLabel.current = null
    setSelectedConnectorId(null)
    if (options.returnFocus) {
      window.requestAnimationFrame(() => connectorLabelRefs.current.get(id)?.focus())
    }
  }

  const deleteConnector = (id: string): void => {
    const current = canvasRef.current
    const connector = current?.connectors.find((item) => item.id === id)
    if (!current || !connector) return
    queueSave({
      ...current,
      connectors: current.connectors.filter((item) => item.id !== id)
    })
    editingOriginalLabel.current = null
    setSelectedConnectorId(null)
    window.requestAnimationFrame(() => {
      cardMoveHandleRefs.current.get(connector.fromCardId)?.focus()
    })
  }

  const createConnector = (
    fromCardId: string,
    toCardId: string,
    fromSide: ConnectorSide,
    toSide: ConnectorSide
  ): void => {
    const current = canvasRef.current
    if (
      !current ||
      fromCardId === toCardId ||
      !current.cards.some((card) => card.id === fromCardId) ||
      !current.cards.some((card) => card.id === toCardId)
    ) {
      return
    }
    const connector: CanvasConnector = {
      id: `conn-${Date.now()}`,
      fromCardId,
      toCardId,
      label: '',
      fromSide,
      toSide
    }
    queueSave({ ...current, connectors: [...current.connectors, connector] })
    editingOriginalLabel.current = { id: connector.id, label: '' }
    setSelectedId(null)
    setKeyboardConnectFrom(null)
    setSelectedConnectorId(connector.id)
  }

  const promote = async (card: CanvasCard): Promise<void> => {
    const current = canvasRef.current
    if (!current || chapters.length === 0) return
    await flushPendingSave()
    const chapterGuid = chapters[0].guid
    const updated = await rpc.request<Canvas | null>('canvas/promoteCard', [
      current.id,
      card.id,
      chapterGuid
    ])
    if (updated) replaceCanvas(updated)
    // The new scene has to reach the binder, or the writer sees the card change
    // colour with nothing to show for it.
    useProjectStore
      .getState()
      .applyState(await rpc.request<ProjectStateDto>('project/getState'))
  }

  const toBoardPoint = (clientX: number, clientY: number): BoardPoint => {
    const surface = surfaceRef.current
    if (!surface) return { x: clientX, y: clientY }
    const bounds = surface.getBoundingClientRect()
    return {
      x: clientX - bounds.left + surface.scrollLeft,
      y: clientY - bounds.top + surface.scrollTop
    }
  }

  const renderedCard = (card: CanvasCard): CanvasCard =>
    dragPosition?.cardId === card.id
      ? { ...card, x: dragPosition.x, y: dragPosition.y }
      : card

  const cardById = (id: string): CanvasCard | undefined => {
    const card = canvas?.cards.find((item) => item.id === id)
    return card ? renderedCard(card) : undefined
  }

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
      Math.hypot(
        event.clientX - gesture.startClientX,
        event.clientY - gesture.startClientY
      ) < DRAG_THRESHOLD_PX
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

  const nudgeCard = (event: React.KeyboardEvent<HTMLButtonElement>, card: CanvasCard): void => {
    const delta: Partial<Record<'x' | 'y', number>> = {}
    if (event.key === 'ArrowLeft') delta.x = -CARD_NUDGE_PX
    else if (event.key === 'ArrowRight') delta.x = CARD_NUDGE_PX
    else if (event.key === 'ArrowUp') delta.y = -CARD_NUDGE_PX
    else if (event.key === 'ArrowDown') delta.y = CARD_NUDGE_PX
    else return
    event.preventDefault()
    updateCard(card.id, {
      x: Math.max(0, card.x + (delta.x ?? 0)),
      y: Math.max(0, card.y + (delta.y ?? 0))
    })
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
      end: target ? pointOnCard(target.card, target.side) : toBoardPoint(event.clientX, event.clientY),
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

  const useConnectorHandleWithKeyboard = (
    event: React.KeyboardEvent<HTMLButtonElement>,
    card: CanvasCard,
    side: ConnectorSide
  ): void => {
    if (event.key === 'Escape') {
      if (keyboardConnectFrom) {
        event.preventDefault()
        setKeyboardConnectFrom(null)
      }
      return
    }
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    if (keyboardConnectFrom) {
      if (keyboardConnectFrom.cardId !== card.id) {
        createConnector(keyboardConnectFrom.cardId, card.id, keyboardConnectFrom.side, side)
        return
      }
      if (keyboardConnectFrom.side === side) {
        setKeyboardConnectFrom(null)
        return
      }
    }
    commitActiveConnectorEdit()
    setSelectedId(card.id)
    setSelectedConnectorId(null)
    setKeyboardConnectFrom({ cardId: card.id, side })
  }

  const connectorHandleLabel = (side: ConnectorSide, card: CanvasCard): string => {
    const cardName = card.title.trim() || t('canvas.untitledCard')
    switch (side) {
      case 'top':
        return t('canvas.connectorHandle.top', { card: cardName })
      case 'right':
        return t('canvas.connectorHandle.right', { card: cardName })
      case 'bottom':
        return t('canvas.connectorHandle.bottom', { card: cardName })
      case 'left':
        return t('canvas.connectorHandle.left', { card: cardName })
    }
  }

  const selected = canvas?.cards.find((card) => card.id === selectedId) ?? null
  const previewFrom = connectorPreview ? cardById(connectorPreview.fromCardId) : undefined
  const previewStart =
    connectorPreview && previewFrom
      ? pointOnCard(previewFrom, connectorPreview.fromSide)
      : null

  return (
    <div className="canvas-view">
      <div className="canvas-toolbar">
        <select
          className="inspector-input"
          value={canvas?.id ?? ''}
          onChange={(event) => void switchBoard(event.target.value)}
          disabled={boards.length === 0}
        >
          {boards.map((board) => (
            <option key={board.id} value={board.id}>
              {board.name}
            </option>
          ))}
        </select>
        <button className="dialog-button" onClick={() => setNaming('create')}>
          <Plus size={14} /> {t('canvas.newBoard')}
        </button>
        <button className="dialog-button" disabled={!canvas} onClick={() => setNaming('rename')}>
          <Pencil size={14} /> {t('canvas.renameBoard')}
        </button>
        <button className="dialog-button" disabled={!canvas} onClick={() => void deleteBoard()}>
          <Trash2 size={14} /> {t('canvas.deleteBoard')}
        </button>
        <button className="dialog-button" disabled={!canvas} onClick={addCard}>
          <Plus size={14} /> {t('canvas.addCard')}
        </button>
        {canvas && canvas.cards.length > 0 && (
          <span className="settings-hint" role="status" aria-live="polite">
            {keyboardConnectFrom
              ? t('canvas.chooseConnectorTarget')
              : t('canvas.connectorHandleHint')}
          </span>
        )}
      </div>

      {!canvas && <p className="settings-hint canvas-empty">{t('canvas.empty')}</p>}

      {canvas && (
        <div
          ref={surfaceRef}
          className="canvas-surface"
          onPointerDown={(event) => {
            if (event.target !== event.currentTarget || event.button !== 0) return
            clearSelection()
            setKeyboardConnectFrom(null)
          }}
        >
          <svg className="canvas-lines" aria-hidden="true">
            {canvas.connectors.map((connector) => {
              const from = cardById(connector.fromCardId)
              const to = cardById(connector.toCardId)
              if (!from || !to) return null
              const points = connectorPoints(connector, from, to)
              return (
                <g
                  key={connector.id}
                  className={`canvas-connector${
                    connector.id === selectedConnectorId ? ' selected' : ''
                  }`}
                  onClick={(event) => {
                    event.stopPropagation()
                    beginConnectorEdit(connector.id)
                  }}
                >
                  <line
                    className="canvas-connector-hit"
                    x1={points.start.x}
                    y1={points.start.y}
                    x2={points.end.x}
                    y2={points.end.y}
                  />
                  <line
                    className="canvas-connector-line"
                    x1={points.start.x}
                    y1={points.start.y}
                    x2={points.end.x}
                    y2={points.end.y}
                  />
                </g>
              )
            })}
            {connectorPreview && previewStart && (
              <line
                className="canvas-connector-preview"
                x1={previewStart.x}
                y1={previewStart.y}
                x2={connectorPreview.end.x}
                y2={connectorPreview.end.y}
              />
            )}
          </svg>

          {canvas.connectors.map((connector) => {
            const from = cardById(connector.fromCardId)
            const to = cardById(connector.toCardId)
            if (!from || !to) return null
            const points = connectorPoints(connector, from, to)
            const midpoint = {
              x: (points.start.x + points.end.x) / 2,
              y: (points.start.y + points.end.y) / 2
            }
            const fromName = from.title.trim() || t('canvas.untitledCard')
            const toName = to.title.trim() || t('canvas.untitledCard')
            const editName = t('canvas.editConnectorBetween', { from: fromName, to: toName })
            const inputName = t('canvas.connectorLabelBetween', {
              from: fromName,
              to: toName
            })
            const editing = connector.id === selectedConnectorId
            return editing ? (
              <div
                key={connector.id}
                className="canvas-connector-label-editor"
                style={connectorOverlayStyle(midpoint)}
                role="group"
                aria-label={editName}
                onPointerDown={(event) => event.stopPropagation()}
                onBlur={(event) => {
                  if (event.currentTarget.contains(event.relatedTarget as Node | null)) return
                  closeConnectorEdit(connector.id, { restore: false, returnFocus: false })
                }}
              >
                <input
                  ref={(node) => {
                    if (node) connectorInputRefs.current.set(connector.id, node)
                    else connectorInputRefs.current.delete(connector.id)
                  }}
                  className="canvas-connector-label-input"
                  value={connector.label}
                  aria-label={inputName}
                  placeholder={t('canvas.addConnectorLabel')}
                  onChange={(event) => updateConnector(connector.id, { label: event.target.value })}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') {
                      event.preventDefault()
                      closeConnectorEdit(connector.id, { restore: false, returnFocus: true })
                    } else if (event.key === 'Escape') {
                      event.preventDefault()
                      closeConnectorEdit(connector.id, { restore: true, returnFocus: true })
                    }
                  }}
                />
                <button
                  type="button"
                  className="canvas-connector-delete"
                  aria-label={t('canvas.deleteConnector')}
                  title={t('canvas.deleteConnector')}
                  onPointerDown={(event) => event.preventDefault()}
                  onClick={() => deleteConnector(connector.id)}
                >
                  <Trash2 size={14} />
                </button>
              </div>
            ) : (
              <button
                key={connector.id}
                ref={(node) => {
                  if (node) connectorLabelRefs.current.set(connector.id, node)
                  else connectorLabelRefs.current.delete(connector.id)
                }}
                type="button"
                className={`canvas-connector-label-display${connector.label ? '' : ' empty'}`}
                style={connectorOverlayStyle(midpoint)}
                aria-label={editName}
                title={connector.label || editName}
                onPointerDown={(event) => event.stopPropagation()}
                onClick={() => beginConnectorEdit(connector.id)}
              >
                {connector.label || t('canvas.addConnectorLabel')}
              </button>
            )
          })}

          {canvas.cards.map((storedCard) => {
            const card = renderedCard(storedCard)
            const cardName = card.title.trim() || t('canvas.untitledCard')
            return (
              <div
                key={card.id}
                data-canvas-card-id={card.id}
                className={`canvas-card${card.id === selectedId ? ' selected' : ''}${
                  card.sceneId ? ' promoted' : ''
                }${connectorPreview?.targetCardId === card.id ? ' connection-target' : ''}${
                  keyboardConnectFrom?.cardId === card.id ? ' connection-source' : ''
                }${card.x < HANDLE_EDGE_GUARD_PX ? ' edge-left' : ''}${
                  card.y < HANDLE_EDGE_GUARD_PX ? ' edge-top' : ''
                }`}
                style={{ left: card.x, top: card.y, width: card.width, height: card.height }}
                onPointerDown={(event) => {
                  if (event.button !== 0) return
                  commitActiveConnectorEdit()
                  setSelectedId(card.id)
                  setSelectedConnectorId(null)
                }}
              >
                <button
                  ref={(node) => {
                    if (node) cardMoveHandleRefs.current.set(card.id, node)
                    else cardMoveHandleRefs.current.delete(card.id)
                  }}
                  type="button"
                  className="canvas-card-move-handle"
                  aria-label={t('canvas.moveCard', { card: cardName })}
                  title={t('canvas.moveCardHint')}
                  onPointerDown={(event) => startCardDrag(event, card)}
                  onPointerMove={moveCardDrag}
                  onPointerUp={finishCardDrag}
                  onPointerCancel={(event) => cancelCardDrag(event.pointerId)}
                  onLostPointerCapture={(event) => cancelCardDrag(event.pointerId)}
                  onKeyDown={(event) => nudgeCard(event, card)}
                >
                  <GripHorizontal size={14} />
                </button>
                <input
                  className="canvas-card-title"
                  value={card.title}
                  placeholder={t('canvas.titlePlaceholder')}
                  onChange={(event) => updateCard(card.id, { title: event.target.value })}
                  onFocus={() => {
                    commitActiveConnectorEdit()
                    setSelectedId(card.id)
                    setSelectedConnectorId(null)
                  }}
                  onPointerDown={(event) => event.stopPropagation()}
                />
                <textarea
                  className="canvas-card-text"
                  value={card.text}
                  placeholder={t('canvas.textPlaceholder')}
                  onChange={(event) => updateCard(card.id, { text: event.target.value })}
                  onFocus={() => {
                    commitActiveConnectorEdit()
                    setSelectedId(card.id)
                    setSelectedConnectorId(null)
                  }}
                  onPointerDown={(event) => event.stopPropagation()}
                />
                {card.sceneId && <span className="canvas-card-badge">{t('canvas.isScene')}</span>}
                {CONNECTOR_SIDES.map((side) => (
                  <button
                    key={side}
                    type="button"
                    data-connector-side={side}
                    className={`canvas-connector-handle ${side}`}
                    aria-label={connectorHandleLabel(side, card)}
                    aria-pressed={
                      keyboardConnectFrom?.cardId === card.id &&
                      keyboardConnectFrom.side === side
                    }
                    title={connectorHandleLabel(side, card)}
                    onPointerDown={(event) => startConnectorDrag(event, card, side)}
                    onPointerMove={moveConnectorDrag}
                    onPointerUp={finishConnectorDrag}
                    onPointerCancel={(event) => cancelConnectorDrag(event.pointerId)}
                    onLostPointerCapture={(event) => cancelConnectorDrag(event.pointerId)}
                    onKeyDown={(event) => useConnectorHandleWithKeyboard(event, card, side)}
                    onClick={(event) => {
                      event.preventDefault()
                      event.stopPropagation()
                    }}
                  />
                ))}
              </div>
            )
          })}
        </div>
      )}

      {selected && (
        <div className="canvas-inspector">
          <button
            className="dialog-button"
            disabled={Boolean(selected.sceneId) || chapters.length === 0}
            onClick={() => void promote(selected)}
          >
            <FileUp size={14} /> {t('canvas.promote')}
          </button>
          <div className="settings-hint">{t('canvas.promoteHint')}</div>
          <button className="dialog-button" onClick={() => deleteCard(selected.id)}>
            <Trash2 size={14} /> {t('canvas.deleteCard')}
          </button>
        </div>
      )}

      {naming && (
        <InputDialog
          title={t(naming === 'create' ? 'canvas.nameBoard' : 'canvas.renameBoard')}
          placeholder={t('canvas.boardNamePlaceholder')}
          initialValue={naming === 'create' ? t('canvas.newBoardName') : (canvas?.name ?? '')}
          onSubmit={(value) => {
            setNaming(null)
            void (naming === 'create' ? createBoard(value) : renameBoard(value))
          }}
          onCancel={() => setNaming(null)}
        />
      )}
    </div>
  )
}
