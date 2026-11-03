import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { useProjectStore, reportManuscriptEditing, type ChapterDto } from './projectStore'
import { useFilterStore } from './filterStore'
import { capturePendingWrites } from './pendingWrites'
import type { ColourDimension } from '../views/manuscript/sceneColour'
import i18n from '../i18n'
import { retainMobileSceneRecovery } from '../mobile/recovery'
import type { SceneDiskChange } from './externalSceneRefresh'

export interface ManuscriptSceneDto {
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

export type ManuscriptMode = 'manuscript' | 'corkboard' | 'outliner' | 'board'

const MANUSCRIPT_AUTOSAVE_MS = 800

interface PendingManuscriptSave {
  timer: ReturnType<typeof setTimeout> | null
  inFlight: Promise<void> | null
  chapterGuid: string
  sceneId: string
  html: string
  plainText: string
  expectedHash: string
  revision: number
}

interface SceneWriteResult {
  sceneId: string
  wordCount: number
  hash: string
  conflicted: boolean
  diskHtml: string | null
}

const saveTimers = new Map<string, PendingManuscriptSave>()
// Accepting a resolved or matching disk version retires every earlier write,
// including predecessors no longer stored as the newest pending payload.
const sceneWriteRevisions = new Map<string, number>()

export function applyExternalManuscriptScene(change: SceneDiskChange, previousHash: string): void {
  const state = useManuscriptStore.getState()
  const scene = state.sections.find(section => section.chapterGuid === change.chapterGuid)?.scenes.find(scene => scene.sceneId === change.sceneId)
  if (!scene || scene.hash !== previousHash || scene.hash === change.hash) return
  const pending = saveTimers.get(change.sceneId)
  if (pending && pending.html !== change.html) {
    if (pending.timer) clearTimeout(pending.timer)
    pending.timer = null
    if (!useProjectStore.getState().sceneConflict) useProjectStore.setState({ sceneConflict: {
      chapterGuid: change.chapterGuid, sceneId: change.sceneId, mine: pending.html,
      theirs: change.html, plainText: pending.plainText
    } })
    return
  }
  state.acceptResolvedScene(change.sceneId, change.html, change.wordCount, change.hash)
}
/** A second debounce may mature while the first payload is still on the wire.
 *  Serialize by scene so its expected hash can be advanced by the predecessor
 *  before the successor is sent. */
const sceneWriteTails = new Map<string, Promise<void>>()
let loadRevision = 0
let pendingLoads = 0
let proseRevision = 0

function writePendingManuscriptSave(save: PendingManuscriptSave): Promise<void> {
  if (save.inFlight) return save.inFlight
  if (save.timer) clearTimeout(save.timer)
  save.timer = null
  const predecessor = sceneWriteTails.get(save.sceneId) ?? Promise.resolve()
  const write = predecessor.catch(() => {}).then(async (): Promise<void> => {
    if (save.revision !== (sceneWriteRevisions.get(save.sceneId) ?? 0)) return
    // Read the base only after our predecessor has settled. It may have
    // advanced the section hash while this save was queued behind it.
    const expectedHash = useManuscriptStore
      .getState()
      .sections.flatMap((section) => section.scenes)
      .find((scene) => scene.sceneId === save.sceneId)?.hash

    const result = await rpc.request<SceneWriteResult>('scenes/write', [
      save.chapterGuid,
      save.sceneId,
      save.html,
      save.plainText,
      expectedHash ?? save.expectedHash
    ])
    if (save.revision !== (sceneWriteRevisions.get(save.sceneId) ?? 0)) return
    const current = saveTimers.get(save.sceneId)
    if (result.conflicted) {
      // Prefer the newest local manuscript payload if the writer typed while
      // this request was in flight. Nothing was written, and the normal merge
      // dialog now owns the explicit choice between both versions.
      const mine = current ?? save
      useProjectStore.setState({
        sceneConflict: {
          chapterGuid: mine.chapterGuid,
          sceneId: mine.sceneId,
          mine: mine.html,
          theirs: result.diskHtml ?? '',
          plainText: mine.plainText
        }
      })
      throw new Error(i18n.t('update.sceneWriteConflict'))
    }

    useManuscriptStore.setState((state) => ({
      sections: state.sections.map((section) => ({
        ...section,
        scenes: section.scenes.map((scene) =>
          scene.sceneId !== save.sceneId
            ? scene
            : current && current !== save
              ? { ...scene, hash: result.hash }
              : { ...scene, html: save.html, hash: result.hash, wordCount: result.wordCount }
        )
      }))
    }))
    useProjectStore
      .getState()
      .applyManuscriptSceneWrite(
        save.chapterGuid,
        save.sceneId,
        save.html,
        save.plainText,
        result.wordCount,
        result.hash
      )
    if (window.novalist.isMobile) {
      const { acknowledgeMobileSceneRecovery } = await import('../mobile/recovery')
      const pending = saveTimers.get(save.sceneId)
      acknowledgeMobileSceneRecovery(save.sceneId, save.html, undefined, pending && pending !== save ? undefined : `manuscript:${save.sceneId}`)
    }
  })
  save.inFlight = write
  const tail = write.catch(() => {})
  sceneWriteTails.set(save.sceneId, tail)
  void tail.then(() => {
    if (sceneWriteTails.get(save.sceneId) === tail) sceneWriteTails.delete(save.sceneId)
  })
  // Keep a rejected payload available for the update button's retry. A newer
  // edit may already have replaced this entry, in which case it wins.
  void write.then(
    () => {
      if (saveTimers.get(save.sceneId) === save) saveTimers.delete(save.sceneId)
      reportManuscriptClaims()
    },
    () => {
      if (saveTimers.get(save.sceneId) === save) save.inFlight = null
    }
  )
  return write
}

interface ManuscriptState {
  mode: ManuscriptMode
  /** When set, only these scenes are composed - a chosen run read as prose. */
  composed: string[] | null
  /** What the board groups by: 'chapter', 'stage', 'pov', or 'prop:<key>'. */
  groupBy: string
  /** A saved list narrowing every mode, or '' for the whole book. */
  filterListId: string
  sections: ManuscriptSectionDto[]
  loaded: boolean
  setMode(mode: ManuscriptMode): void
  /**
   * Whether the corkboard is arranged by the writer rather than by reading
   * order. Session state, not a setting: it is a way of looking at the book,
   * and the arrangement itself is what persists.
   */
  freeform: boolean
  setFreeform(freeform: boolean): void
  /**
   * What a corkboard card's edge colour means. Session state like freeform:
   * it is a way of looking at the book, not a property of it.
   */
  colourBy: ColourDimension
  setColourBy(colourBy: ColourDimension): void
  setGroupBy(groupBy: string): void
  compose(sceneIds: string[] | null): Promise<void>
  /** Narrows every mode to a saved list's scenes; '' is the whole book. */
  applyList(filterListId: string): Promise<void>
  load(): Promise<void>
  onSceneContentChanged(sceneId: string, html: string, plainText: string, wordCount: number): void
  /** Writes every scene still waiting in the manuscript editor's debounce. */
  flushPendingSave(): Promise<void>
  reconcileSceneStructure(chapters: ChapterDto[]): void
  /** Adopts a conflict resolution and retires the manuscript payload it chose
   *  between, so it cannot be submitted again after the merge. */
  acceptResolvedScene(sceneId: string, html: string, wordCount: number, hash: string): void
  setSynopsis(chapterGuid: string, sceneId: string, synopsis: string): Promise<void>
  setPov(chapterGuid: string, sceneId: string, pov: string): Promise<void>
  setGoalOutcome(
    chapterGuid: string,
    sceneId: string,
    goal: string,
    outcome: string
  ): Promise<void>
}

export const useManuscriptStore = create<ManuscriptState>((set, get) => ({
  mode: 'manuscript',
  freeform: false,
  composed: null,
  groupBy: 'stage',
  filterListId: '',
  sections: [],
  loaded: false,

  setMode: (mode) => set({ mode }),
  setFreeform: (freeform) => set({ freeform }),
  colourBy: 'label',
  setColourBy: (colourBy) => set({ colourBy }),

  setGroupBy: (groupBy) => set({ groupBy }),

  /**
   * Narrows every mode to the scenes a saved list finds - the outliner, the
   * corkboard and the board as well as the prose. A saved list that only the
   * binder could apply is a question you can ask in one place and nowhere
   * else, which is most of the point of saving it.
   */
  applyList: async (filterListId) => {
    if (!filterListId) {
      set({ filterListId: '', composed: null })
      await get().load()
      return
    }
    const matches = await rpc.request<{ sceneId: string }[]>('smartLists/evaluate', [filterListId])
    set({ filterListId, composed: matches.map((m) => m.sceneId) })
    await get().load()
  },

  load: async () => {
    const revision = ++loadRevision
    pendingLoads++
    try {
      while (revision === loadRevision) {
        // The iframe and the store each debounce prose. Drain both before a
        // filter can remove their scenes or replace their text with disk data.
        capturePendingWrites()
        await get().flushPendingSave()
        if (revision !== loadRevision) return
        capturePendingWrites()
        if (hasPendingManuscriptWrites()) continue

        const shared = useFilterStore.getState().filter
        const { composed, filterListId } = get()
        const readProseRevision = proseRevision
        // The RPC's empty chosen set means "whole book" for older callers.
        // An active saved query with zero matches must instead stay empty.
        const sections = filterListId && composed?.length === 0 ? [] :
          await rpc.request<ManuscriptSectionDto[]>('manuscript/get', [
            shared.status || 'All', composed,
            shared.character || null, shared.location || null,
            shared.plotline || null, shared.stage || null
          ])
        if (revision !== loadRevision) return
        // Typing can continue during the read. Save those newer words and
        // fetch again instead of publishing a snapshot that predates them.
        capturePendingWrites()
        if (hasPendingManuscriptWrites() || readProseRevision !== proseRevision) continue
        set({ sections, loaded: true })
        return
      }
    } finally {
      pendingLoads--
    }
  },

  reconcileSceneStructure: (chapters) => {
    // A read started before the move/archive cannot put the old arrangement
    // back after its successful mutation has been reconciled.
    const replaceInitialLoad = !get().loaded && pendingLoads > 0
    loadRevision++
    const scenes = new Map(get().sections.flatMap((section) =>
      section.scenes.map((scene) => [scene.sceneId, scene] as const)))
    const present = new Set(chapters.flatMap((chapter) => chapter.scenes.map((scene) => scene.id)))
    set({
      sections: chapters.flatMap((chapter) => {
        const remaining = chapter.scenes.flatMap((scene) => {
          const content = scenes.get(scene.id)
          return content ? [content] : []
        })
        return remaining.length ? [{
          chapterGuid: chapter.guid,
          chapterTitle: chapter.title,
          status: chapter.status,
          act: chapter.act,
          scenes: remaining
        }] : []
      }),
      composed: get().composed?.filter((id) => present.has(id)) ?? null
    })
    if (replaceInitialLoad && !useProjectStore.getState().workspaceBusy) void get().load()
  },

  // Reading a chosen run as continuous prose - one POV's thread, the scenes a
  // saved list found - is the only way to hear whether it holds together.
  compose: async (composed) => {
    set({ composed, mode: 'manuscript', filterListId: '' })
    await get().load()
  },

  onSceneContentChanged: (sceneId, html, plainText, wordCount) => {
    const section = get().sections.find((s) => s.scenes.some((sc) => sc.sceneId === sceneId))
    if (!section) return
    proseRevision++
    set({
      sections: get().sections.map((s) => ({
        ...s,
        scenes: s.scenes.map((sc) => (sc.sceneId === sceneId ? { ...sc, html, wordCount } : sc))
      }))
    })
    const existing = saveTimers.get(sceneId)
    if (existing?.timer) clearTimeout(existing.timer)
    const save: PendingManuscriptSave = {
      timer: null,
      inFlight: null,
      chapterGuid: section.chapterGuid,
      sceneId,
      html,
      plainText,
      expectedHash: section.scenes.find((scene) => scene.sceneId === sceneId)?.hash ?? '',
      revision: sceneWriteRevisions.get(sceneId) ?? 0
    }
    save.timer = setTimeout(() => {
      void writePendingManuscriptSave(save).catch(() => {
        // The payload stays in saveTimers for an explicit retry, while a scene
        // conflict is surfaced through projectStore's merge dialog.
      })
    }, MANUSCRIPT_AUTOSAVE_MS)
    saveTimers.set(sceneId, save)
    retainMobileSceneRecovery({ source: `manuscript:${sceneId}`, chapterGuid: save.chapterGuid, sceneId, html, plainText, hash: save.expectedHash })
    reportManuscriptClaims()
  },

  flushPendingSave: async () => {
    if (useProjectStore.getState().sceneConflict) {
      throw new Error(i18n.t('update.unsavedConflict'))
    }
    const pending = [...saveTimers.values()]
    for (const save of pending) {
      await writePendingManuscriptSave(save)
    }
    // Another concurrently flushed editor can be the path that discovers the
    // conflict. A manuscript flush still cannot certify a safe shutdown merely
    // because its own queue happened to empty first.
    if (useProjectStore.getState().sceneConflict) {
      throw new Error(i18n.t('update.unsavedConflict'))
    }
  },

  acceptResolvedScene: (sceneId, html, wordCount, hash) => {
    sceneWriteRevisions.set(sceneId, (sceneWriteRevisions.get(sceneId) ?? 0) + 1)
    const pending = saveTimers.get(sceneId)
    if (pending?.timer) clearTimeout(pending.timer)
    saveTimers.delete(sceneId)
    set({
      sections: get().sections.map((section) => ({
        ...section,
        scenes: section.scenes.map((scene) =>
          scene.sceneId === sceneId ? { ...scene, html, wordCount, hash } : scene
        )
      }))
    })
    reportManuscriptClaims()
  },

  setSynopsis: async (chapterGuid, sceneId, synopsis) => {
    await rpc.request('scenes/setSynopsis', [chapterGuid, sceneId, synopsis])
    set({
      sections: get().sections.map((s) =>
        s.chapterGuid === chapterGuid
          ? {
              ...s,
              scenes: s.scenes.map((sc) =>
                sc.sceneId === sceneId ? { ...sc, synopsis: synopsis || null } : sc
              )
            }
          : s
      )
    })
  },

  setPov: async (chapterGuid, sceneId, pov) => {
    await rpc.request('scenes/setPov', [chapterGuid, sceneId, pov])
    set({
      sections: get().sections.map((s) =>
        s.chapterGuid === chapterGuid
          ? {
              ...s,
              scenes: s.scenes.map((sc) => (sc.sceneId === sceneId ? { ...sc, pov: pov || null } : sc))
            }
          : s
      )
    })
  },

  setGoalOutcome: async (chapterGuid, sceneId, goal, outcome) => {
    await rpc.request('scenes/setGoalOutcome', [chapterGuid, sceneId, goal, outcome])
    set({
      sections: get().sections.map((s) =>
        s.chapterGuid === chapterGuid
          ? {
              ...s,
              scenes: s.scenes.map((sc) =>
                sc.sceneId === sceneId
                  ? { ...sc, goal: goal || null, outcome: outcome || null }
                  : sc
              )
            }
          : s
      )
    })
  }
}))

function reportManuscriptClaims(): void {
  reportManuscriptEditing([...saveTimers.values()].map(({ chapterGuid, sceneId }) => ({ chapterGuid, sceneId, dirty: true })))
}

export function pendingManuscriptRecovery(): { chapterGuid: string; sceneId: string; html: string; plainText: string; hash: string }[] {
  return [...saveTimers.values()].map((save) => ({ chapterGuid: save.chapterGuid, sceneId: save.sceneId, html: save.html, plainText: save.plainText, hash: save.expectedHash }))
}

export function hasPendingManuscriptWrites(): boolean { return saveTimers.size > 0 }

/** Called only after the shared workspace has acknowledged every pending save. */
export function resetManuscriptWorkspace(): void {
  loadRevision++
  useManuscriptStore.setState({ sections: [], loaded: false, composed: null, filterListId: '' })
  reportManuscriptClaims()
}
