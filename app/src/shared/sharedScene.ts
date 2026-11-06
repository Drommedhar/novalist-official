export interface SharedSceneIdentity {
  sessionId: string
  itemId: string
  state: string
  presence?: boolean
}

export interface SharedSceneUpdate {
  sessionId: string
  itemId: string
  update: string
}
