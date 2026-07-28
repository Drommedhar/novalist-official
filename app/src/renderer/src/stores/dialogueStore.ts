import { create } from 'zustand'
import { rpc } from '../rpc/client'

// Mirrors the DialogueRpc DTOs (camelCase over the wire).

/** Sentinel speaker id for lines no character could be attributed to. Matches
 *  DialogueIndexService.UnassignedSpeakerId; not a valid entity id, so it can
 *  never collide with one. */
export const UNASSIGNED_SPEAKER_ID = '?unassigned'

/** How firmly a line is tied to its speaker. "Manual" is the writer's own
 *  assignment; "Low" is a guess from alternation alone. */
export type DialogueConfidence = 'Manual' | 'High' | 'Inferred' | 'Medium' | 'Low' | 'None'

/** Another character the line might belong to, with their share of the
 *  evidence. Shares sum to 100 across a line's candidates. */
export interface DialogueCandidate {
  characterId: string
  percent: number
}

export interface DialogueLine {
  lineKey: string
  text: string
  confidence: DialogueConfidence
  /** False when the line carries markup (emphasis, a mention, a footnote) and
   *  so can only be changed in the editor without destroying it. */
  editable: boolean
  contextBefore: string
  contextAfter: string
  /** Ranked alternatives, empty where the prose names the speaker outright. */
  candidates: DialogueCandidate[]
}

export interface DialogueScene {
  chapterGuid: string
  sceneId: string
  chapterTitle: string
  sceneTitle: string
  storyDate: string
  lines: DialogueLine[]
}

/** A run of scenes at one point in story time. `storyDate` is blank for the run
 *  before any date is known. */
export interface DialogueGroup {
  storyDate: string
  scenes: DialogueScene[]
}

export interface DialogueSpeaker {
  characterId: string
  name: string
  lineCount: number
}

/** A character the writer can reassign a line to — the whole cast, including
 *  those with no attributed lines yet. */
export interface DialogueCharacter {
  id: string
  name: string
}

interface DialogueIndex {
  speakers: DialogueSpeaker[]
  characters: DialogueCharacter[]
  unassignedCount: number
  selectedId: string | null
  groups: DialogueGroup[]
}

interface DialogueUpdateResult {
  status: 'Updated' | 'Stale' | 'NotEditable'
  lineKey: string | null
}

interface DialogueState {
  speakers: DialogueSpeaker[]
  characters: DialogueCharacter[]
  unassignedCount: number
  selectedId: string | null
  groups: DialogueGroup[]
  loading: boolean
  /** Set when a save was refused because the scene changed underneath us. The
   *  view shows it and the writer reloads. */
  staleError: boolean
  load(characterId?: string | null): Promise<void>
  select(characterId: string): Promise<void>
  setSpeaker(
    chapterGuid: string,
    sceneId: string,
    lineKey: string,
    characterId: string | null
  ): Promise<void>
  updateLine(
    chapterGuid: string,
    sceneId: string,
    lineKey: string,
    originalText: string,
    newText: string
  ): Promise<boolean>
  clearStaleError(): void
  reset(): void
}

export const useDialogueStore = create<DialogueState>((set, get) => ({
  speakers: [],
  characters: [],
  unassignedCount: 0,
  selectedId: null,
  groups: [],
  loading: false,
  staleError: false,

  load: async (characterId) => {
    set({ loading: true })
    const result = await rpc.request<DialogueIndex>('dialogue/index', [
      characterId ?? get().selectedId ?? null
    ])
    set({
      speakers: result.speakers,
      characters: result.characters,
      unassignedCount: result.unassignedCount,
      selectedId: result.selectedId,
      groups: result.groups,
      loading: false
    })
  },

  select: async (characterId) => {
    if (get().selectedId === characterId) return
    set({ selectedId: characterId, groups: [] })
    await get().load(characterId)
  },

  // Reassigning a speaker moves the line between characters, so the whole index
  // is rebuilt rather than patched — the counts and the other character's list
  // both change.
  setSpeaker: async (chapterGuid, sceneId, lineKey, characterId) => {
    await rpc.request<boolean>('dialogue/setSpeaker', [
      chapterGuid,
      sceneId,
      lineKey,
      characterId
    ])
    await get().load()
  },

  updateLine: async (chapterGuid, sceneId, lineKey, originalText, newText) => {
    const result = await rpc.request<DialogueUpdateResult>('dialogue/updateLine', [
      chapterGuid,
      sceneId,
      lineKey,
      originalText,
      newText
    ])
    if (result.status !== 'Updated') {
      set({ staleError: true })
      return false
    }

    // Patch the edited line in place so the list does not jump under the cursor
    // while the writer works down a scene. The key can change with the text.
    const newKey = result.lineKey ?? lineKey
    set({
      groups: get().groups.map((group) => ({
        ...group,
        scenes: group.scenes.map((scene) =>
          scene.sceneId === sceneId
            ? {
                ...scene,
                lines: scene.lines.map((line) =>
                  line.lineKey === lineKey
                    ? { ...line, lineKey: newKey, text: newText.trim() }
                    : line
                )
              }
            : scene
        )
      }))
    })
    return true
  },

  clearStaleError: () => set({ staleError: false }),

  reset: () =>
    set({
      speakers: [],
      characters: [],
      unassignedCount: 0,
      selectedId: null,
      groups: [],
      staleError: false
    })
}))
