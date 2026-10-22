export type SceneCommand =
  | { kind: 'seed'; id: string; html: string }
  | { kind: 'seedText'; id: string; text: string }
  | { kind: 'load'; id: string; state: Uint8Array; deletions?: Uint8Array[]; plainText?: boolean }
  | { kind: 'receive' | 'remove' | 'validate'; id: string; state: Uint8Array }

export interface SharedSceneSnapshot {
  state: Uint8Array
  deletions: Uint8Array[]
  html: string | null
  recovered: string | null
  text?: string | null
  recoveredText?: string | null
}

export interface DocumentRequest {
  sequence: number
  command: SceneCommand | { kind: 'capture'; id: string }
}

export interface SceneApplyResult {
  accepted: boolean
  rejected: string[]
}

export type DocumentReply =
  | { sequence: number; failed: true }
  | { sequence: number; failed: false; snapshot?: SharedSceneSnapshot; application?: SceneApplyResult }
