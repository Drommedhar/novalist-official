export const MAP_WRITE_KEY = 'maps:document'

export interface MapRefDto {
  id: string
  name: string
}

export interface EntityOption {
  id: string
  type: string
  name: string
}

export interface PeekData {
  name: string
  detail: string
  imageUrl: string | null
}

export interface Loading3D {
  progress: number
  status: string
}

export const MAP_AUTOSAVE_MS = 1200

export const IMAGE_BASE_URL = 'novalist-project://nl/'

export interface MapMessage {
  type: string
  x?: number
  y?: number
  imageId?: string
  entityId?: string
  entityType?: string
  step?: string
  /** A pin that opens another map carries its id. */
  targetMapId?: string
  /** Ruler result: raw world units, and the same in the declared unit. */
  worldUnits?: number
  ground?: number
  unit?: string
}
