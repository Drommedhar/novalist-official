import { create } from 'zustand'
import { rpc } from '../rpc/client'
import { useEditorBridge } from '../stores/editorBridgeStore'
import { useProjectStore } from '../stores/projectStore'
import { useSettingsStore } from '../stores/settingsStore'
import { useShellStore } from '../stores/shellStore'
import { registerPendingWrite } from '../stores/pendingWrites'
import type { EditorWindow } from '../views/editor/editorBridge'
import type { ReplacementRule } from '../views/settings/AutoReplacementsCard'
import { audioBase64, openMicrophone, type Microphone } from './audio'
import { dictationQuotes, formatDictation, type DictationSegment, type DictationInsertion, type QuotePair } from './formatDictation'

export interface DictationProvider {
  id: string; name: string; available: boolean; audioDestination: string; formattingDestination: string
  automaticDialogue?: boolean; usesSystemPanel?: boolean
  languages?: { language: string; supported: boolean; installed: boolean }[]
}
interface State {
  open: boolean
  providers: DictationProvider[]
  providerId: string
  language: 'en' | 'de'
  recording: boolean
  starting: boolean
  pending: number
  paused: boolean
  error: string | null
  warning: string | null
}
export const useDictation = create<State>(() => ({
  open: false, providers: [], providerId: '', language: 'en', recording: false,
  starting: false, pending: 0, paused: false, error: null, warning: null
}))
const set = useDictation.setState
type Clip = { audio: Uint8Array; transcript?: string; insertion?: DictationInsertion }
type Session = {
  id: string; target: string; editor: EditorWindow; providerId: string; language: string
  quotes: QuotePair; queue: Clip[]; context: string; lastKind?: DictationSegment['kind']
  microphone?: Microphone; abort: AbortController; requestId?: string; processing?: Promise<void>; stopping?: Promise<void>
  automaticDialogue: boolean
}
let session: Session | null = null

function target(): string {
  const p = useProjectStore.getState()
  return JSON.stringify([p.projectPath, p.activeBookId, p.drafts.find((d) => d.isActive)?.id,
    useEditorBridge.getState().sceneId])
}
function atTarget(s: Session): boolean {
  const shell = useShellStore.getState()
  return target() === s.target && useEditorBridge.getState().editor === s.editor
    && shell.mainView === 'write' && !shell.extView
}

export async function showDictation(): Promise<void> {
  installLifecycle()
  if (useDictation.getState().recording || useDictation.getState().starting) { await stopDictation(); return }
  set({ open: true })
  if (session) return
  try {
    const providers = await rpc.request<DictationProvider[]>('dictation/providers')
    const writing = useSettingsStore.getState().view?.effective.autoReplacementLanguage ?? 'en'
    set({ providers, providerId: providers.find((p) => p.available)?.id ?? providers[0]?.id ?? '',
      language: writing.startsWith('de') ? 'de' : 'en', error: null })
  } catch { set({ error: 'dictation.unavailable' }) }
}

export async function startDictation(): Promise<void> {
  if (session || useDictation.getState().starting) return
  const state = useDictation.getState()
  const editor = useEditorBridge.getState().editor
  const provider = state.providers.find((p) => p.id === state.providerId)
  if (!editor || !canDictate(provider, state.language)) return
  if (provider?.usesSystemPanel) {
    set({ starting: true, error: null, warning: null })
    try {
      // The OS types into the focused contenteditable. Reestablish the saved
      // caret after the writer used the provider selector or command palette.
      if (!editor.focusDictationCaret()) throw new Error('No editor caret')
      await rpc.request('dictation/openSystemPanel')
      set({ open: false })
    } catch { set({ error: 'dictation.systemPanelFailed' }) }
    finally { set({ starting: false }) }
    return
  }
  const settings = useSettingsStore.getState().view
  const rules = (settings?.overrides?.autoReplacements ?? settings?.global.autoReplacements ?? []) as ReplacementRule[]
  const s: Session = { id: crypto.randomUUID(), target: target(), editor, providerId: state.providerId,
    language: state.language, quotes: dictationQuotes(settings?.effective.autoReplacementLanguage ?? 'en', rules),
    queue: [], context: '', abort: new AbortController(), automaticDialogue: provider?.automaticDialogue !== false }
  if (!editor.captureDictationAnchor(s.id)) return
  session = s
  set({ starting: true, error: null, warning: null, paused: false })
  try {
    s.microphone = await openMicrophone((audio) => {
      if (session !== s) return
      s.queue.push({ audio })
      set({ pending: s.queue.length })
      if (s.queue.length >= 12) {
        set({ warning: 'dictation.backlog' })
        void stopDictation()
      }
      void drain(s)
    }, () => { set({ warning: 'dictation.microphoneEnded' }); void stopDictation() }, s.abort.signal)
    if (session !== s || s.abort.signal.aborted || !atTarget(s)) {
      await s.microphone.stop(); s.microphone = undefined; return
    }
    set({ recording: true })
  } catch (error) {
    if (session === s && !s.abort.signal.aborted) set({ error: error instanceof DOMException && error.name === 'NotAllowedError'
      ? 'dictation.permission' : 'dictation.microphoneError' })
    if (!s.queue.length && session === s) { session = null; set({ starting: false }) }
  } finally {
    if (session === s) set({ starting: false })
    finish(s)
  }
}

export async function stopDictation(): Promise<void> {
  const s = session
  if (!s) return
  if (s.stopping) return s.stopping
  set({ recording: false, starting: false })
  // Stop flushes the final partial clip before processing finishes.
  s.stopping = Promise.resolve().then(async () => {
    const microphone = s.microphone
    s.microphone = undefined
    if (microphone) await microphone.stop()
    else s.abort.abort()
    await drain(s)
  })
  try { await s.stopping } finally { s.stopping = undefined; finish(s) }
}

function finish(s: Session): void {
  if (session === s && !s.microphone && !s.stopping && !useDictation.getState().starting && !s.queue.length && !s.processing) {
    session = null
    set({ pending: 0, paused: false })
  }
}

async function drain(s: Session): Promise<void> {
  if (s.processing) return s.processing
  if (session !== s || useDictation.getState().paused || useDictation.getState().error) return
  s.processing = (async () => {
    while (session === s && s.queue.length && !useDictation.getState().paused) {
      if (!atTarget(s)) { set({ paused: true, warning: 'dictation.sceneChanged' }); break }
      const clip = s.queue[0]
      try {
        if (clip.transcript === undefined) {
          s.requestId = crypto.randomUUID()
          clip.transcript = await rpc.request<string>('dictation/transcribe', { requestId: s.requestId,
            providerId: s.providerId, audioBase64: audioBase64(clip.audio), mimeType: 'audio/wav', language: s.language })
        }
        if (session !== s) break
        if (!clip.transcript.trim()) { s.queue.shift(); set({ pending: s.queue.length }); continue }
        if (!clip.insertion) {
          if (!s.automaticDialogue) {
            clip.insertion = { text: clip.transcript, paragraph: false, mergeClose: '', lastKind: 'narration' }
          } else {
            try {
              s.requestId = crypto.randomUUID()
              const segments = await rpc.request<DictationSegment[]>('dictation/format', { requestId: s.requestId,
                providerId: s.providerId, transcript: clip.transcript, language: s.language, precedingText: s.context.slice(-2000) })
              clip.insertion = formatDictation(segments, s.quotes, s.language, s.lastKind)
              if (!clip.insertion.text.trim()) throw new Error('Empty formatting result')
            } catch {
              clip.insertion = { text: clip.transcript, paragraph: false, mergeClose: '', lastKind: 'narration' }
              if (session === s) set({ warning: 'dictation.formatFailed' })
            }
          }
        }
        if (session !== s) break
        if (!atTarget(s) || useDictation.getState().paused) {
          set({ paused: true, warning: 'dictation.sceneChanged' }); break
        }
        const insert = clip.insertion
        if (!s.editor.insertDictationText(s.id, insert.text, insert.paragraph, insert.mergeClose, s.quotes.open, insert.tag)) {
          set({ paused: true, warning: 'dictation.anchorLost' }); void stopDictation(); break
        }
        s.editor.flushPendingContentChange()
        s.context = (s.context + '\n' + insert.text).slice(-2000)
        s.lastKind = insert.lastKind
        s.queue.shift()
        set({ pending: s.queue.length })
      } catch {
        if (session === s) {
          set({ error: 'dictation.transcribeFailed' })
          void stopDictation()
        }
        break
      } finally { s.requestId = undefined }
    }
  })()
  try { await s.processing } finally { s.processing = undefined; finish(s) }
}

export function canDictate(provider: DictationProvider | undefined, language: string): boolean {
  if (!provider?.available) return false
  if (provider.usesSystemPanel || !provider.languages) return true
  return provider.languages.some((entry) => entry.language === language && entry.supported && entry.installed)
}

export async function resumeDictation(): Promise<void> {
  const s = session
  if (!s || target() !== s.target) { set({ warning: 'dictation.returnToScene' }); return }
  const editor = useEditorBridge.getState().editor
  if (!editor) return
  if (useDictation.getState().paused) {
    s.editor = editor
    editor.captureDictationAnchor(s.id)
    // A new anchor need not follow the text inserted before the pause.
    s.lastKind = undefined
    const first = s.queue[0]?.insertion
    if (first?.mergeClose) { first.text = s.quotes.open + first.text; first.mergeClose = ''; first.paragraph = true }
    if (first) first.tag = undefined
  }
  set({ paused: false, error: null, warning: null })
  await drain(s)
}

export async function discardDictation(): Promise<void> {
  const s = session
  session = null
  if (s) {
    s.abort.abort()
    if (s.requestId) void rpc.request('dictation/cancel', { requestId: s.requestId }).catch(() => {})
    await s.microphone?.stop()
  }
  set({ recording: false, starting: false, pending: 0, paused: false, error: null, warning: null })
}

function checkTarget(): void {
  if (session && !atTarget(session)) {
    set({ paused: true, warning: 'dictation.sceneChanged' })
    void stopDictation()
  }
}
let installed = false
function installLifecycle(): void {
  if (installed) return
  installed = true
  useProjectStore.subscribe(checkTarget)
  useEditorBridge.subscribe(checkTarget)
  useShellStore.subscribe(checkTarget)
  window.addEventListener('pagehide', () => { session?.abort.abort() })
  // Project switches and update shutdown wait for captured speech.
  registerPendingWrite(async () => {
    await stopDictation()
    if (session?.queue.length) throw new Error('Dictation still has pending speech. Resume or discard it before closing.')
    useEditorBridge.getState().editor?.flushPendingContentChange()
  })
}
