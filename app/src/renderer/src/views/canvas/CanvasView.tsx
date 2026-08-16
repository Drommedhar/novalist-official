import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link2, Plus, Trash2, FileUp, Pencil } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { InputDialog } from '../../shell/InputDialog'
import { useProjectStore, type ProjectStateDto } from '../../stores/projectStore'
import './canvas.css'

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

/** Autosave delay, matching the editor's. */
const SAVE_DELAY_MS = 2000

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
  const [connectFrom, setConnectFrom] = useState<string | null>(null)
  /** Which name the writer is being asked for, if any. */
  const [naming, setNaming] = useState<'create' | 'rename' | null>(null)
  const dragRef = useRef<{ id: string; dx: number; dy: number } | null>(null)
  const saveTimer = useRef<number | null>(null)

  const loadBoards = useCallback(async () => {
    const list = await rpc.request<CanvasSummary[]>('canvas/list')
    setBoards(list)
    if (list.length > 0 && !canvas) {
      setCanvas(await rpc.request<Canvas | null>('canvas/load', [list[0].id]))
    }
  }, [canvas])

  useEffect(() => {
    void loadBoards()
    // Only on mount: loadBoards closes over `canvas` to avoid clobbering an
    // open board, and re-running on every change would fight the editor.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  /** Queues a save. Every mutation goes through here so nothing is lost. */
  const queueSave = useCallback((next: Canvas) => {
    setCanvas(next)
    if (saveTimer.current) window.clearTimeout(saveTimer.current)
    saveTimer.current = window.setTimeout(() => {
      void rpc.request('canvas/save', [next])
    }, SAVE_DELAY_MS)
  }, [])

  // Flush a pending save when leaving the view, so a board is never lost by
  // navigating away inside the debounce window.
  useEffect(
    () => () => {
      if (saveTimer.current) window.clearTimeout(saveTimer.current)
    },
    []
  )

  const createBoard = async (name: string): Promise<void> => {
    const created = await rpc.request<Canvas>('canvas/create', [name])
    setCanvas(created)
    await loadBoards()
  }

  /**
   * Renames the open board.
   *
   * Written through straight away rather than queued: the name is what the
   * board is picked by in the list beside it, and a name that only appears two
   * seconds later reads as the rename having failed. Any queued card edits are
   * carried along in the same write, so nothing is lost by jumping the debounce.
   */
  const renameBoard = async (name: string): Promise<void> => {
    if (!canvas) return
    const renamed = { ...canvas, name }
    if (saveTimer.current) window.clearTimeout(saveTimer.current)
    saveTimer.current = null
    setCanvas(renamed)
    await rpc.request('canvas/save', [renamed])
    setBoards(await rpc.request<CanvasSummary[]>('canvas/list'))
  }

  const switchBoard = async (id: string): Promise<void> => {
    setSelectedId(null)
    setConnectFrom(null)
    setCanvas(await rpc.request<Canvas | null>('canvas/load', [id]))
  }

  // Deleting the open board drops any queued save with it, otherwise the
  // debounce would rewrite the file we just removed.
  const deleteBoard = async (): Promise<void> => {
    if (!canvas) return
    if (!window.confirm(t('canvas.deleteBoardConfirm', { name: canvas.name }))) return
    if (saveTimer.current) window.clearTimeout(saveTimer.current)
    saveTimer.current = null
    await rpc.request<boolean>('canvas/delete', [canvas.id])
    setSelectedId(null)
    setConnectFrom(null)
    const list = await rpc.request<CanvasSummary[]>('canvas/list')
    setBoards(list)
    setCanvas(
      list.length > 0 ? await rpc.request<Canvas | null>('canvas/load', [list[0].id]) : null
    )
  }

  const addCard = (): void => {
    if (!canvas) return
    const card: CanvasCard = {
      id: `card-${Date.now()}`,
      title: '',
      text: '',
      // Dropped near the viewport origin rather than at (0,0), so a new card is
      // visible without hunting for it.
      x: 80 + canvas.cards.length * 24,
      y: 80 + canvas.cards.length * 16,
      width: 200,
      height: 120,
      color: '',
      sceneId: '',
      chapterGuid: '',
      entityId: ''
    }
    queueSave({ ...canvas, cards: [...canvas.cards, card] })
    setSelectedId(card.id)
  }

  const updateCard = (id: string, patch: Partial<CanvasCard>): void => {
    if (!canvas) return
    queueSave({
      ...canvas,
      cards: canvas.cards.map((c) => (c.id === id ? { ...c, ...patch } : c))
    })
  }

  const deleteCard = (id: string): void => {
    if (!canvas) return
    queueSave({
      ...canvas,
      cards: canvas.cards.filter((c) => c.id !== id),
      // A connector to a card that no longer exists would draw to nowhere.
      connectors: canvas.connectors.filter((c) => c.fromCardId !== id && c.toCardId !== id)
    })
    setSelectedId(null)
  }

  const connect = (toId: string): void => {
    if (!canvas || !connectFrom || connectFrom === toId) {
      setConnectFrom(null)
      return
    }
    queueSave({
      ...canvas,
      connectors: [
        ...canvas.connectors,
        { id: `conn-${Date.now()}`, fromCardId: connectFrom, toCardId: toId, label: '' }
      ]
    })
    setConnectFrom(null)
  }

  const promote = async (card: CanvasCard): Promise<void> => {
    if (!canvas || chapters.length === 0) return
    const chapterGuid = chapters[0].guid
    const updated = await rpc.request<Canvas | null>('canvas/promoteCard', [
      canvas.id,
      card.id,
      chapterGuid
    ])
    if (updated) setCanvas(updated)
    // The new scene has to reach the binder, or the writer sees the card change
    // colour with nothing to show for it.
    useProjectStore
      .getState()
      .applyState(await rpc.request<ProjectStateDto>('project/getState'))
  }

  const onPointerDown = (e: React.PointerEvent, card: CanvasCard): void => {
    if (connectFrom) {
      connect(card.id)
      return
    }
    setSelectedId(card.id)
    dragRef.current = { id: card.id, dx: e.clientX - card.x, dy: e.clientY - card.y }
    ;(e.target as Element).setPointerCapture(e.pointerId)
  }

  const onPointerMove = (e: React.PointerEvent): void => {
    const drag = dragRef.current
    if (!drag) return
    updateCard(drag.id, { x: e.clientX - drag.dx, y: e.clientY - drag.dy })
  }

  const onPointerUp = (): void => {
    dragRef.current = null
  }

  const selected = canvas?.cards.find((c) => c.id === selectedId) ?? null
  const cardById = (id: string): CanvasCard | undefined => canvas?.cards.find((c) => c.id === id)

  return (
    <div className="canvas-view">
      <div className="canvas-toolbar">
        <select
          className="inspector-input"
          value={canvas?.id ?? ''}
          onChange={(e) => void switchBoard(e.target.value)}
          disabled={boards.length === 0}
        >
          {boards.map((b) => (
            <option key={b.id} value={b.id}>
              {b.name}
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
        <button
          className={`dialog-button${connectFrom ? ' active' : ''}`}
          disabled={!selected}
          onClick={() => setConnectFrom(selectedId)}
        >
          <Link2 size={14} /> {t('canvas.connect')}
        </button>
        {connectFrom && <span className="settings-hint">{t('canvas.connectHint')}</span>}
      </div>

      {!canvas && <p className="settings-hint canvas-empty">{t('canvas.empty')}</p>}

      {canvas && (
        <div className="canvas-surface" onPointerMove={onPointerMove} onPointerUp={onPointerUp}>
          <svg className="canvas-lines">
            {canvas.connectors.map((conn) => {
              const from = cardById(conn.fromCardId)
              const to = cardById(conn.toCardId)
              if (!from || !to) return null
              const x1 = from.x + from.width / 2
              const y1 = from.y + from.height / 2
              const x2 = to.x + to.width / 2
              const y2 = to.y + to.height / 2
              return (
                <g key={conn.id}>
                  <line x1={x1} y1={y1} x2={x2} y2={y2} />
                  {conn.label && (
                    <text x={(x1 + x2) / 2} y={(y1 + y2) / 2}>
                      {conn.label}
                    </text>
                  )}
                </g>
              )
            })}
          </svg>

          {canvas.cards.map((card) => (
            <div
              key={card.id}
              className={`canvas-card${card.id === selectedId ? ' selected' : ''}${
                card.sceneId ? ' promoted' : ''
              }`}
              style={{ left: card.x, top: card.y, width: card.width, height: card.height }}
              onPointerDown={(e) => onPointerDown(e, card)}
            >
              <input
                className="canvas-card-title"
                value={card.title}
                placeholder={t('canvas.titlePlaceholder')}
                onChange={(e) => updateCard(card.id, { title: e.target.value })}
                onPointerDown={(e) => e.stopPropagation()}
              />
              <textarea
                className="canvas-card-text"
                value={card.text}
                placeholder={t('canvas.textPlaceholder')}
                onChange={(e) => updateCard(card.id, { text: e.target.value })}
                onPointerDown={(e) => e.stopPropagation()}
              />
              {card.sceneId && <span className="canvas-card-badge">{t('canvas.isScene')}</span>}
            </div>
          ))}
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
