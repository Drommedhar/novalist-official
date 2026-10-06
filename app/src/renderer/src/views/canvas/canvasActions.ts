import { rpc } from '../../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../../stores/projectStore'
import { persistPendingWrite } from '../../stores/pendingWrites'
import { type CanvasCard, type Canvas, type CanvasSummary, CARD_NUDGE_PX } from './canvasTypes'
import type { useCanvasState, useCanvasSaving, useCanvasConnectorCommit } from './canvasState'

export function createCanvasSelection(context: CanvasSelectionContext) {
  const {
    dragFrame, dragPositionRef, cardDragRef, connectorDragRef, setDragPosition,
    setConnectorPreview, setKeyboardConnectFrom, commitActiveConnectorEdit,
    editingOriginalLabel, setSelectedId, setSelectedConnectorId
  } = context
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
  return { clearTransientGestures, clearSelection }
}

export function createCanvasBoards(context: CanvasBoardsContext) {
  const {
    commitActiveConnectorEdit, flushPendingSave, clearTransientGestures, clearSelection,
    replaceCanvas, loadBoards, canvasRef, setBoards, t, saveTimer, pendingSave
  } = context
  const createBoard = async (name: string): Promise<void> => {
    commitActiveConnectorEdit()
    await flushPendingSave()
    const created = await rpc.request<Canvas>('canvas/create', [name])
    clearTransientGestures()
    clearSelection()
    replaceCanvas(created)
    await loadBoards()
  }

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
  return { createBoard, renameBoard, switchBoard, deleteBoard }
}

export function createCanvasCards(context: CanvasCardsContext) {
  const {
    commitActiveConnectorEdit, canvasRef, queueSave, setSelectedId, setSelectedConnectorId,
    pendingTitles, setKeyboardConnectFrom, setConnectorPreview, clearSelection, chapters,
    flushPendingSave, replaceCanvas
  } = context
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
    if (patch.title !== undefined) pendingTitles.current.set(id, patch.title)
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
    useProjectStore.getState().applyState(await rpc.request<ProjectStateDto>('project/getState'))
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
  return { addCard, updateCard, deleteCard, promote, nudgeCard }
}

type CanvasSelectionContext = Pick<ReturnType<typeof useCanvasState>, 'dragFrame' | 'dragPositionRef' | 'cardDragRef' | 'connectorDragRef' | 'setDragPosition' | 'setConnectorPreview' | 'setKeyboardConnectFrom' | 'editingOriginalLabel' | 'setSelectedId' | 'setSelectedConnectorId'> &
  Pick<ReturnType<typeof useCanvasConnectorCommit>, 'commitActiveConnectorEdit'>

type CanvasBoardsContext = Pick<ReturnType<typeof useCanvasConnectorCommit>, 'commitActiveConnectorEdit'> &
  Pick<ReturnType<typeof useCanvasSaving>, 'flushPendingSave'> &
  Pick<ReturnType<typeof createCanvasSelection>, 'clearTransientGestures' | 'clearSelection'> &
  Pick<ReturnType<typeof useCanvasState>, 'replaceCanvas' | 'loadBoards' | 'canvasRef' | 'setBoards' | 't' | 'saveTimer' | 'pendingSave'>

type CanvasCardsContext = Pick<ReturnType<typeof useCanvasConnectorCommit>, 'commitActiveConnectorEdit'> &
  Pick<ReturnType<typeof useCanvasState>, 'canvasRef' | 'setSelectedId' | 'setSelectedConnectorId' | 'pendingTitles' | 'setKeyboardConnectFrom' | 'setConnectorPreview' | 'chapters' | 'replaceCanvas'> &
  Pick<ReturnType<typeof useCanvasSaving>, 'queueSave' | 'flushPendingSave'> &
  Pick<ReturnType<typeof createCanvasSelection>, 'clearSelection'>
