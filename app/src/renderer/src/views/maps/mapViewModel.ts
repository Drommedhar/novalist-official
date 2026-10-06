import { useMapViewState } from './mapViewState'
import { useMapPersistence } from './mapPersistence'
import { useMapStrings, useMapLoading, useMapSubscriptions } from './mapLoading'
import { useMapSelection, useMapMessages } from './mapMessaging'
import { useMapTools, useMapImages } from './mapToolActions'
import { useMapLayerSelection, useMapLayerCreation, useMapNodeEditing, useMapNodeView } from './mapLayerActions'
import { useMapListActions, createMapDocumentActions } from './mapActions'

export function useMapViewModel() {
  const state = useMapViewState()
  const persistence = useMapPersistence(state)
  const strings = useMapStrings({ ...state, ...persistence })
  const loading = useMapLoading({ ...state, ...persistence })
  const subscriptions = useMapSubscriptions({ ...state, ...persistence, ...loading })
  const selection = useMapSelection({ ...state, ...persistence })
  const messages = useMapMessages({ ...state, ...persistence, ...strings, ...loading, ...selection })
  const tools = useMapTools({ ...state, ...persistence })
  const images = useMapImages({ ...state, ...persistence })
  const layerSelection = useMapLayerSelection({ ...state, ...persistence })
  const layerCreation = useMapLayerCreation({ ...state, ...persistence, ...layerSelection })
  const nodeEditing = useMapNodeEditing({ ...state, ...persistence })
  const nodeView = useMapNodeView({ ...state, ...persistence })
  const listActions = useMapListActions({ ...state, ...persistence })
  const documentActions = createMapDocumentActions({ ...state, ...loading })
  return {
    ...state, ...persistence, ...strings, ...loading, ...subscriptions, ...selection,
    ...messages, ...tools, ...images, ...layerSelection, ...layerCreation, ...nodeEditing,
    ...nodeView, ...listActions, ...documentActions
  }
}

export type MapsViewState = ReturnType<typeof useMapViewModel>
