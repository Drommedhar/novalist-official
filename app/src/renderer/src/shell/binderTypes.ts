export const STATUS_CYCLE = ['Outline', 'FirstDraft', 'Revised', 'Edited', 'Final']

/**
 * How the scenes inside a chapter are ordered.
 *
 * Reading order is the book. The rest are ways of looking for something -
 * the longest scene, the one whose title you half remember, everything still
 * at the same stage - and none of them is the order the book is in, which is
 * why dragging is off while one is active. A drag under a title sort would
 * write a reorder nobody meant.
 */
export const SORT_MODES = ['order', 'title', 'words', 'stage'] as const
export type SortMode = (typeof SORT_MODES)[number]

export interface BinderPlotline {
  id: string
  name: string
  color: string
}

export interface MenuState {
  x: number
  y: number
  chapterGuid: string
  sceneId: string | null
}

export type PendingAction =
  | { kind: 'editChapter'; chapterGuid: string }
  | { kind: 'sceneTemplate'; chapterGuid: string; sceneId: string; title: string }
  | { kind: 'editScene'; chapterGuid: string; sceneId: string; current: string }
  | { kind: 'deleteChapter'; chapterGuid: string; title: string }
  | { kind: 'deleteScene'; chapterGuid: string; sceneId: string; title: string }
  | { kind: 'setDate'; chapterGuid: string; sceneId: string }
  | { kind: 'setAct'; chapterGuid: string; current: string }
  | { kind: 'sceneTarget'; targets: { chapterGuid: string; sceneId: string }[]; current: string }
  | { kind: 'deleteScenes'; targets: { chapterGuid: string; sceneId: string }[] }
  | { kind: 'chapterTarget'; chapterGuid: string; current: string }
  | { kind: 'actTarget'; actName: string; current: string }
  | { kind: 'insertChapter'; beforeOrder: number }
  | { kind: 'chapterDescription'; chapterGuid: string; current: string }

export interface ArchivedScene {
  id: string
  title: string
  wordCount: number
  /** The chapter it left, by title. Empty when that chapter is gone. */
  originChapterTitle: string
}

export interface TrashedChapter {
  guid: string
  title: string
  deletedAt: string
  sceneCount: number
}
