import { pointOnCard } from './canvasGeometry'
import { useCanvasState, useCanvasSaving, useCanvasConnectorCommit, useCanvasLifecycle } from './canvasState'
import { createCanvasSelection, createCanvasBoards, createCanvasCards } from './canvasActions'
import { createCanvasConnectorEditing, createCanvasConnection } from './canvasConnections'
import { createCanvasCoordinates, createCanvasCardDragging, createCanvasConnectorDragging } from './canvasGestures'

export function useCanvasView() {
  const state = useCanvasState()
  const saving = useCanvasSaving(state)
  const commit = useCanvasConnectorCommit({ ...state, ...saving })
  const editingContext = { ...state, ...saving, ...commit }
  useCanvasLifecycle(editingContext)
  const selection = createCanvasSelection(editingContext)
  const boards = createCanvasBoards({ ...editingContext, ...selection })
  const cards = createCanvasCards({ ...editingContext, ...selection })
  const connectorEditing = createCanvasConnectorEditing(editingContext)
  const coordinates = createCanvasCoordinates(state)
  const connection = createCanvasConnection(editingContext)
  const cardDragging = createCanvasCardDragging({ ...editingContext, ...cards, ...coordinates })
  const connectorDragging = createCanvasConnectorDragging({ ...editingContext, ...coordinates, ...connection })
  const { selectedId, connectorPreview } = state
  const { cardById } = coordinates
  const selected = selectedId ? (cardById(selectedId) ?? null) : null
  const previewFrom = connectorPreview ? cardById(connectorPreview.fromCardId) : undefined
  const previewStart =
    connectorPreview && previewFrom ? pointOnCard(previewFrom, connectorPreview.fromSide) : null
  return {
    ...editingContext,
    ...selection,
    ...boards,
    ...cards,
    ...connectorEditing,
    ...coordinates,
    ...connection,
    ...cardDragging,
    ...connectorDragging,
    selected,
    previewStart
  }
}

export type CanvasViewState = ReturnType<typeof useCanvasView>
