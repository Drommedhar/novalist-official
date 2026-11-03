import type { SharedSceneIdentity } from '../../../shared/sharedScene'

export interface ManuscriptSceneDto {
  shared?: SharedSceneIdentity
  sceneId: string
  title: string
  html: string
  /** Fingerprint of the exact prose this editor loaded. */
  hash: string
  wordCount: number
  synopsis: string | null
  pov: string | null
  /** What the viewpoint wants here. Authored, never inferred. */
  goal: string | null
  /** What they are left with. Authored, never inferred. */
  outcome: string | null
  /** True when the scene is out of the book but still in the plan. */
  inactive: boolean
}

export interface ManuscriptSectionDto {
  chapterGuid: string
  chapterTitle: string
  status: string
  act: string
  scenes: ManuscriptSceneDto[]
}
