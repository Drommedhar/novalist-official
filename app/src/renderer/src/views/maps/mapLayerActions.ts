import { type MapViewState } from './mapViewState'
import { useCallback } from 'react'
import { deleteNode as deleteNodeInTree, findNode, moveNode, newId, type DropPosition, type ElementKind } from './mapModel'
import { useMapPersistence } from './mapPersistence'

type MapLayerSelectionContext = Pick<MapViewState, 'setSelectedNodeId' | 'setExpanded' | 'mapModelRef'> &
  Pick<ReturnType<typeof useMapPersistence>, 'getWin'>

export function useMapLayerSelection(context: MapLayerSelectionContext) {
  const {
    setSelectedNodeId, getWin, setExpanded, mapModelRef
  } = context
  const onSelectNode = useCallback(
    (id: string): void => {
      setSelectedNodeId(id)
      getWin()?.setActiveLayer(id)
    },
    [getWin]
  )

  const onToggleExpand = useCallback((id: string): void => {
    setExpanded((prev) => {
      const model = mapModelRef.current
      const node = model ? findNode(model, id) : null
      const current = prev[id] ?? node?.expanded ?? true
      return { ...prev, [id]: !current }
    })
  }, [])
  return { onSelectNode, onToggleExpand }
}

type MapLayerCreationContext = Pick<ReturnType<typeof useMapPersistence>, 'commitMap'> &
  Pick<ReturnType<typeof useMapLayerSelection>, 'onSelectNode'> &
  Pick<MapViewState, 'setExpanded'>

export function useMapLayerCreation(context: MapLayerCreationContext) {
  const {
    commitMap, onSelectNode, setExpanded
  } = context
  const onAddLayer = useCallback((): void => {
    let newNodeId = ''
    commitMap((data) => {
      newNodeId = newId('layer')
      data.layers.push({
        id: newNodeId,
        name: `Layer ${data.layers.length + 1}`,
        opacity: 1,
        locked: false,
        hidden: false,
        expanded: true,
        images: [],
        children: []
      })
    })
    if (newNodeId) onSelectNode(newNodeId)
  }, [commitMap, onSelectNode])

  const onAddChild = useCallback(
    (parentId: string): void => {
      let newNodeId = ''
      commitMap((data) => {
        const parent = findNode(data, parentId)
        if (!parent) return
        parent.children = parent.children ?? []
        newNodeId = newId('layer')
        parent.children.push({
          id: newNodeId,
          name: `Layer ${parent.children.length + 1}`,
          opacity: 1,
          locked: false,
          hidden: false,
          expanded: true,
          images: [],
          children: []
        })
        parent.expanded = true
      })
      setExpanded((prev) => ({ ...prev, [parentId]: true }))
      if (newNodeId) onSelectNode(newNodeId)
    },
    [commitMap, onSelectNode]
  )
  return { onAddLayer, onAddChild }
}

type MapNodeEditingContext = Pick<ReturnType<typeof useMapPersistence>, 'commitMap'> &
  Pick<MapViewState, 'setSelectedNodeId'>

export function useMapNodeEditing(context: MapNodeEditingContext) {
  const {
    commitMap, setSelectedNodeId
  } = context
  const onDeleteNode = useCallback(
    (id: string): void => {
      commitMap((data) => {
        deleteNodeInTree(data, id)
      })
      setSelectedNodeId((prev) => (prev === id ? null : prev))
    },
    [commitMap]
  )

  const onRenameNode = useCallback(
    (id: string, name: string): void => {
      commitMap((data) => {
        const node = findNode(data, id)
        if (node) node.name = name
      })
    },
    [commitMap]
  )

  const onToggleHidden = useCallback(
    (id: string): void => {
      commitMap((data) => {
        const node = findNode(data, id)
        if (node) node.hidden = !node.hidden
      })
    },
    [commitMap]
  )

  const onToggleLocked = useCallback(
    (id: string): void => {
      commitMap((data) => {
        const node = findNode(data, id)
        if (node) node.locked = !node.locked
      })
    },
    [commitMap]
  )

  const onMoveNode = useCallback(
    (dragId: string, targetId: string, pos: DropPosition): void => {
      commitMap((data) => {
        moveNode(data, dragId, targetId, pos)
      })
    },
    [commitMap]
  )

  const onMoveToRoot = useCallback(
    (dragId: string): void => {
      commitMap((data) => {
        const node = findNode(data, dragId)
        if (!node) return
        deleteNodeInTree(data, dragId)
        data.layers.push(node)
      })
    },
    [commitMap]
  )

  const onSetOpacity = useCallback(
    (id: string, opacity: number): void => {
      const clamped = Math.round(Math.max(0, Math.min(1, opacity)) * 100) / 100
      commitMap((data) => {
        const node = findNode(data, id)
        if (node) node.opacity = clamped
      }, 400)
    },
    [commitMap]
  )
  return { onDeleteNode, onRenameNode, onToggleHidden, onToggleLocked, onMoveNode, onMoveToRoot, onSetOpacity }
}

type MapNodeViewContext = Pick<ReturnType<typeof useMapPersistence>, 'commitMap' | 'getWin'> &
  Pick<MapViewState, 'isolated' | 'setIsolated'>

export function useMapNodeView(context: MapNodeViewContext) {
  const {
    commitMap, getWin, isolated, setIsolated
  } = context
  const onSetNodeZoom = useCallback(
    (id: string, min: number, max: number): void => {
      commitMap((data) => {
        const node = findNode(data, id)
        if (!node) return
        node.minZoom = min > 0 ? min : null
        node.maxZoom = max > 0 ? max : null
      }, 400)
    },
    [commitMap]
  )

  const onSetFloorMode = useCallback(
    (id: string, on: boolean): void => {
      commitMap((data) => {
        const node = findNode(data, id)
        if (!node) return
        node.isConnectedSet = on
        if (on && !node.defaultMemberLayerId && node.children?.length)
          node.defaultMemberLayerId = node.children[0].id
      })
    },
    [commitMap]
  )

  const onSetActiveFloor = useCallback(
    (id: string, memberId: string): void => {
      commitMap((data) => {
        const node = findNode(data, id)
        if (node) node.defaultMemberLayerId = memberId || null
      })
    },
    [commitMap]
  )

  const onSetElementZoom = useCallback(
    (kind: ElementKind, id: string, min: number, max: number): void => {
      const win = getWin()
      if (!win) return
      // Images use updateImageZoomRange; every other kind goes through
      // setElementZoomRange (map.html's elementById() has no image case). Both
      // emit mapChanged, which refreshes the panel model and persists.
      if (kind === 'image') win.updateImageZoomRange(id, min, max)
      else win.setElementZoomRange(kind, id, min, max)
    },
    [getWin]
  )

  const onToggleIsolate = useCallback(
    (kind: ElementKind, id: string): void => {
      const win = getWin()
      if (!win) return
      const nowOn = !(isolated && isolated.kind === kind && isolated.id === id)
      if (kind === 'image') win.setIsolatedImage(nowOn ? id : '')
      else win.setIsolatedElement(nowOn ? kind : '', nowOn ? id : '')
      setIsolated(nowOn ? { kind, id } : null)
    },
    [getWin, isolated]
  )
  return { onSetNodeZoom, onSetFloorMode, onSetActiveFloor, onSetElementZoom, onToggleIsolate }
}
