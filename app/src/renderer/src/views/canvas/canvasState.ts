import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../../stores/projectStore'
import { persistPendingWrite, registerPendingWrite } from '../../stores/pendingWrites'
import { type ConnectorSide, type BoardPoint, type Canvas, type CanvasSummary, type CardDragGesture, type ConnectorDragGesture, type ConnectorPreview, SAVE_DELAY_MS } from './canvasTypes'

/**
 * Freeform planning board: loose cards and author-drawn labelled connectors on
 * an infinite surface.
 *
 * Nothing here is part of the manuscript. A card only becomes a scene when the
 * writer promotes it, which is what keeps the board usable for half-formed
 * ideas that should not yet count towards a word target.
 */
export function useCanvasState() {
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

  const [naming, setNaming] = useState<'create' | 'rename' | null>(null)

  const surfaceRef = useRef<HTMLDivElement | null>(null)

  const canvasRef = useRef<Canvas | null>(null)

  const cardDragRef = useRef<CardDragGesture | null>(null)

  const connectorDragRef = useRef<ConnectorDragGesture | null>(null)

  const dragPositionRef = useRef<(BoardPoint & { cardId: string }) | null>(null)

  const dragFrame = useRef<number | null>(null)

  const saveTimer = useRef<number | null>(null)

  const pendingSave = useRef<Canvas | null>(null)

  const pendingTitles = useRef(new Map<string, string>())

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
  return {
    t, chapters, boards, setBoards, canvas, setCanvas, selectedId, setSelectedId,
    selectedConnectorId, setSelectedConnectorId, keyboardConnectFrom, setKeyboardConnectFrom,
    connectorPreview, setConnectorPreview, dragPosition, setDragPosition, naming, setNaming,
    surfaceRef, canvasRef, cardDragRef, connectorDragRef, dragPositionRef, dragFrame,
    saveTimer, pendingSave, pendingTitles, inFlightSave, connectorInputRefs,
    connectorLabelRefs, cardMoveHandleRefs, editingOriginalLabel, replaceCanvas, loadBoards
  }
}

export function useCanvasSaving(context: CanvasSavingContext) {
  const {
    saveTimer, inFlightSave, pendingSave, pendingTitles, replaceCanvas
  } = context
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
      const titles = Object.fromEntries(pendingTitles.current)
      const request = persistPendingWrite(`canvas:${pending.id}`, async () => {
        await rpc.request('canvas/save', [pending, titles])
        if (pendingSave.current === pending) pendingSave.current = null
        for (const [id, title] of Object.entries(titles)) {
          if (pendingTitles.current.get(id) === title) pendingTitles.current.delete(id)
        }
        if (Object.keys(titles).length > 0) {
          useProjectStore
            .getState()
            .applyState(await rpc.request<ProjectStateDto>('project/getState'))
        }
      })
      inFlightSave.current = request
      try {
        await request
      } finally {
        if (inFlightSave.current === request) inFlightSave.current = null
      }
    }
  }, [])

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
  return { flushPendingSave, queueSave }
}

export function useCanvasConnectorCommit(context: CanvasConnectorCommitContext) {
  const {
    editingOriginalLabel, canvasRef, queueSave
  } = context
  const commitActiveConnectorEdit = useCallback(
    (restore = false): void => {
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
    },
    [queueSave]
  )
  return { commitActiveConnectorEdit }
}

export function useCanvasLifecycle(context: CanvasLifecycleContext) {
  const {
    loadBoards, commitActiveConnectorEdit, flushPendingSave, dragFrame, cardDragRef,
    connectorDragRef, selectedConnectorId, connectorInputRefs, keyboardConnectFrom,
    setKeyboardConnectFrom
  } = context
  useEffect(() => {
    void loadBoards()
  }, [loadBoards])

  useEffect(
    () =>
      registerPendingWrite(async () => {
        commitActiveConnectorEdit()
        await flushPendingSave()
      }),
    [commitActiveConnectorEdit, flushPendingSave]
  )

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
}

type CanvasSavingContext = Pick<ReturnType<typeof useCanvasState>, 'saveTimer' | 'inFlightSave' | 'pendingSave' | 'pendingTitles' | 'replaceCanvas'>

type CanvasConnectorCommitContext = Pick<ReturnType<typeof useCanvasState>, 'editingOriginalLabel' | 'canvasRef'> &
  Pick<ReturnType<typeof useCanvasSaving>, 'queueSave'>

type CanvasLifecycleContext = Pick<ReturnType<typeof useCanvasState>, 'loadBoards' | 'dragFrame' | 'cardDragRef' | 'connectorDragRef' | 'selectedConnectorId' | 'connectorInputRefs' | 'keyboardConnectFrom' | 'setKeyboardConnectFrom'> &
  Pick<ReturnType<typeof useCanvasConnectorCommit>, 'commitActiveConnectorEdit'> &
  Pick<ReturnType<typeof useCanvasSaving>, 'flushPendingSave'>
