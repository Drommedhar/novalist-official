import { type ConnectorSide, type CanvasCard, type CanvasConnector } from './canvasTypes'
import type { useCanvasState, useCanvasSaving, useCanvasConnectorCommit } from './canvasState'

export function createCanvasConnectorEditing(context: CanvasConnectorEditingContext) {
  const {
    canvasRef, editingOriginalLabel, commitActiveConnectorEdit, setSelectedId,
    setKeyboardConnectFrom, setSelectedConnectorId, connectorInputRefs, queueSave,
    connectorLabelRefs, cardMoveHandleRefs
  } = context
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
  return { beginConnectorEdit, updateConnector, closeConnectorEdit, deleteConnector }
}

export function createCanvasConnection(context: CanvasConnectionContext) {
  const {
    canvasRef, queueSave, editingOriginalLabel, setSelectedId, setKeyboardConnectFrom,
    setSelectedConnectorId, keyboardConnectFrom, commitActiveConnectorEdit, t
  } = context
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

  const handleConnectorKeyboard = (
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
  return { createConnector, handleConnectorKeyboard, connectorHandleLabel }
}

type CanvasConnectorEditingContext = Pick<ReturnType<typeof useCanvasState>, 'canvasRef' | 'editingOriginalLabel' | 'setSelectedId' | 'setKeyboardConnectFrom' | 'setSelectedConnectorId' | 'connectorInputRefs' | 'connectorLabelRefs' | 'cardMoveHandleRefs'> &
  Pick<ReturnType<typeof useCanvasConnectorCommit>, 'commitActiveConnectorEdit'> &
  Pick<ReturnType<typeof useCanvasSaving>, 'queueSave'>

type CanvasConnectionContext = Pick<ReturnType<typeof useCanvasState>, 'canvasRef' | 'editingOriginalLabel' | 'setSelectedId' | 'setKeyboardConnectFrom' | 'setSelectedConnectorId' | 'keyboardConnectFrom' | 't'> &
  Pick<ReturnType<typeof useCanvasSaving>, 'queueSave'> &
  Pick<ReturnType<typeof useCanvasConnectorCommit>, 'commitActiveConnectorEdit'>
