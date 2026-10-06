import { GripHorizontal, Trash2 } from 'lucide-react'
import { CONNECTOR_SIDES, HANDLE_EDGE_GUARD_PX } from './canvasTypes'
import { connectorPoints, connectorOverlayStyle } from './canvasGeometry'
import { type CanvasViewState } from './canvasModel'



export function CanvasConnectorLabels({
  canvas,
  cardById,
  t,
  selectedConnectorId,
  closeConnectorEdit,
  connectorInputRefs,
  updateConnector,
  deleteConnector,
  connectorLabelRefs,
  beginConnectorEdit
}: CanvasConnectorLabelsProps): React.JSX.Element {
  return <>{canvas.connectors.map((connector) => {
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
      })}</>
}

export function CanvasCards({
  canvas,
  renderedCard,
  t,
  selectedId,
  connectorPreview,
  keyboardConnectFrom,
  commitActiveConnectorEdit,
  setSelectedId,
  setSelectedConnectorId,
  cardMoveHandleRefs,
  startCardDrag,
  moveCardDrag,
  finishCardDrag,
  cancelCardDrag,
  nudgeCard,
  updateCard,
  connectorHandleLabel,
  startConnectorDrag,
  moveConnectorDrag,
  finishConnectorDrag,
  cancelConnectorDrag,
  useConnectorHandleWithKeyboard
}: CanvasCardsProps): React.JSX.Element {
  return <>{canvas.cards.map((storedCard) => {
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
                  keyboardConnectFrom?.cardId === card.id && keyboardConnectFrom.side === side
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
      })}</>
}

export function CanvasLines({
  canvas,
  cardById,
  selectedConnectorId,
  beginConnectorEdit,
  connectorPreview,
  previewStart
}: CanvasLinesProps): React.JSX.Element {
  return (
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
  )
}

type CanvasSurfaceState = CanvasViewState

type CanvasConnectorLabelsProps = {
  canvas: NonNullable<CanvasViewState['canvas']>
  cardById: CanvasViewState['cardById']
  t: CanvasViewState['t']
  selectedConnectorId: CanvasViewState['selectedConnectorId']
  closeConnectorEdit: CanvasViewState['closeConnectorEdit']
  connectorInputRefs: CanvasViewState['connectorInputRefs']
  updateConnector: CanvasViewState['updateConnector']
  deleteConnector: CanvasViewState['deleteConnector']
  connectorLabelRefs: CanvasViewState['connectorLabelRefs']
  beginConnectorEdit: CanvasViewState['beginConnectorEdit']
}

type CanvasCardsProps = {
  canvas: NonNullable<CanvasViewState['canvas']>
  renderedCard: CanvasViewState['renderedCard']
  t: CanvasViewState['t']
  selectedId: CanvasViewState['selectedId']
  connectorPreview: CanvasViewState['connectorPreview']
  keyboardConnectFrom: CanvasViewState['keyboardConnectFrom']
  commitActiveConnectorEdit: CanvasViewState['commitActiveConnectorEdit']
  setSelectedId: CanvasViewState['setSelectedId']
  setSelectedConnectorId: CanvasViewState['setSelectedConnectorId']
  cardMoveHandleRefs: CanvasViewState['cardMoveHandleRefs']
  startCardDrag: CanvasViewState['startCardDrag']
  moveCardDrag: CanvasViewState['moveCardDrag']
  finishCardDrag: CanvasViewState['finishCardDrag']
  cancelCardDrag: CanvasViewState['cancelCardDrag']
  nudgeCard: CanvasViewState['nudgeCard']
  updateCard: CanvasViewState['updateCard']
  connectorHandleLabel: CanvasViewState['connectorHandleLabel']
  startConnectorDrag: CanvasViewState['startConnectorDrag']
  moveConnectorDrag: CanvasViewState['moveConnectorDrag']
  finishConnectorDrag: CanvasViewState['finishConnectorDrag']
  cancelConnectorDrag: CanvasViewState['cancelConnectorDrag']
  useConnectorHandleWithKeyboard: CanvasViewState['useConnectorHandleWithKeyboard']
}

type CanvasLinesProps = {
  canvas: NonNullable<CanvasSurfaceState['canvas']>
  cardById: CanvasSurfaceState['cardById']
  selectedConnectorId: CanvasSurfaceState['selectedConnectorId']
  beginConnectorEdit: CanvasSurfaceState['beginConnectorEdit']
  connectorPreview: CanvasSurfaceState['connectorPreview']
  previewStart: CanvasSurfaceState['previewStart']
}
