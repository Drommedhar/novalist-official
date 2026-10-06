import { DesktopViewActions } from '../../shell/DesktopViewFrame'
import { FileUp, Pencil, Plus, Trash2 } from 'lucide-react'
import { InputDialog } from '../../shell/InputDialog'
import './canvas.css'
import { useCanvasView, type CanvasViewState } from './canvasModel'
import { CanvasLines, CanvasConnectorLabels, CanvasCards } from './CanvasSurface'
import { useContentTransition } from '../../shell/useContentTransition'
import { MotionPresence } from '../../shell/MotionPresence'

export function CanvasView(): React.JSX.Element {
  const model = useCanvasView()
  const {
    canvas,
    t,
    selected,
    chapters,
    promote,
    deleteCard,
    naming,
    setNaming,
    createBoard,
    renameBoard
  } = model
  return (
    <div className="canvas-view">
      <CanvasToolbar {...model} />

      {!canvas && <p className="settings-hint canvas-empty">{t('canvas.empty')}</p>}

      {canvas && (
        <CanvasSurface model={model} canvas={canvas} />
      )}

      <MotionPresence>{selected && (
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
      )}</MotionPresence>

      <MotionPresence>{naming && (
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
      )}</MotionPresence>
    </div>
  )
}

function CanvasToolbar({
  canvas,
  switchBoard,
  boards,
  setNaming,
  t,
  deleteBoard,
  addCard,
  keyboardConnectFrom
}: CanvasToolbarProps): React.JSX.Element {
  return (
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
      <DesktopViewActions>
        {' '}
        <button className="dialog-button primary" disabled={!canvas} onClick={addCard}>
          <Plus size={14} /> {t('canvas.addCard')}
        </button>
      </DesktopViewActions>
      {canvas && canvas.cards.length > 0 && (
        <span className="settings-hint" role="status" aria-live="polite">
          {keyboardConnectFrom
            ? t('canvas.chooseConnectorTarget')
            : t('canvas.connectorHandleHint')}
        </span>
      )}
    </div>
  )
}

function CanvasSurface({ model, canvas }: {
  model: CanvasViewState
  canvas: NonNullable<CanvasViewState['canvas']>
}): React.JSX.Element {
  const { surfaceRef, clearSelection, setKeyboardConnectFrom } = model
  useContentTransition(surfaceRef, canvas.id)
  return (
    <div
      ref={surfaceRef}
      className="canvas-surface"
      onPointerDown={(event) => {
        if (event.target !== event.currentTarget || event.button !== 0) return
        clearSelection()
        setKeyboardConnectFrom(null)
      }}
    >
      <CanvasLines {...model} canvas={canvas} />
      <CanvasConnectorLabels {...model} canvas={canvas} />
      <CanvasCards {...model} canvas={canvas} />
    </div>
  )
}

type CanvasToolbarProps = Pick<CanvasViewState,
  | 'canvas'
  | 'switchBoard'
  | 'boards'
  | 'setNaming'
  | 't'
  | 'deleteBoard'
  | 'addCard'
  | 'keyboardConnectFrom'
>
