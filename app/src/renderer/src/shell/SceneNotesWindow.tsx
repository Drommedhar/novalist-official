import { useEffect, useRef, useState } from 'react'
import { flushSync } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { EyeOff, ListChecks, Pin, PinOff, PanelsTopLeft } from 'lucide-react'
import { type SceneNotesWindowState } from '../../../shared/sceneNotesProtocol'
import { useProjectStore } from '../stores/projectStore'
import { useUiScaleStore } from '../stores/uiScaleStore'
import { flushPendingWrites } from '../stores/pendingWrites'
import { rpc } from '../rpc/client'
import { hydrateWindow } from './bootstrap'
import { useWorkspaceWindow } from './useWorkspaceWindow'
import { WorkspaceRecoveryDialog } from './WorkspaceRecoveryDialog'
import { WindowClosingDialog } from './WindowClosingDialog'
import { SceneChecklistView } from './SceneChecklistView'
import { registerSharedProjectNotifications } from '../collaboration/sharedProjectNotifications'
import { useHostedSessionStore } from '../collaboration/hostedSessionStore'
import './sceneChecklist.css'

export function SceneNotesWindow(): React.JSX.Element {
  const { t } = useTranslation()
  const closeStage = useWorkspaceWindow()
  const project = useProjectStore()
  const [windowState, setWindowState] = useState<SceneNotesWindowState>({ context: null, pinned: true, visible: false, docked: false, follow: true })
  const [released, setReleased] = useState(false)
  const [transferring, setTransferring] = useState(false)
  const stateVersion = useRef(0)
  const [ready, setReady] = useState(false)
  const [error, setError] = useState(false)
  const context = windowState.context
  useEffect(() => {
    document.title = t('sceneChecklist.title')
  }, [t])
  useEffect(() => {
    useUiScaleStore.getState().apply()
    // This renderer does not mount the main shell's host bridge or toolbar.
    registerSharedProjectNotifications()
    let active = true
    let received = false
    const stop = window.novalist.onSceneNotesState?.((state) => { received = true; stateVersion.current++; setWindowState(state); setReleased(false) })
    void window.novalist.sceneNotesWindowState?.().then((state) => { if (active && !received) setWindowState(state) })
    const hydrate = async (): Promise<void> => {
      try {
        await useHostedSessionStore.getState().refresh(() => active)
        await hydrateWindow()
        if (active) { setReady(true); setError(false) }
      }
      catch { if (active) setError(true) }
    }
    const stopReconnect = rpc.onReconnected(() => { void hydrate() })
    void rpc.connect().then(hydrate).catch(() => { if (active) setError(true) })
    return () => { active = false; stop?.(); stopReconnect() }
  }, [])
  useEffect(() => {
    if (!ready || project.workspaceBusy || project.workspaceSuspended) return
    let active = true
    let timer: ReturnType<typeof setTimeout>
    const refresh = async (): Promise<void> => {
      await useHostedSessionStore.getState().refresh(() => active)
      if (active) timer = setTimeout(() => { void refresh() }, 2000)
    }
    void refresh()
    return () => { active = false; clearTimeout(timer) }
  }, [ready, project.projectPath, project.workspaceEpoch, project.workspaceBusy, project.workspaceSuspended])
  useEffect(() => window.novalist.onSceneNotesRelease?.(async () => {
    const version = stateVersion.current
    flushSync(() => setTransferring(true))
    try {
      await flushPendingWrites()
      if (version !== stateVersion.current) throw new Error('Notes transfer cancelled')
      flushSync(() => setReleased(true))
      await flushPendingWrites()
    } catch (reason) { setReleased(false); setError(true); throw reason }
    finally { setTransferring(false) }
  }), [])
  useEffect(() => {
    const hide = (): void => { void flushPendingWrites().then(() => window.novalist.hideSceneNotes?.()).catch(() => setError(true)) }
    const keydown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape' || ((event.ctrlKey || event.metaKey) && event.shiftKey && event.key.toLowerCase() === 'n')) {
        event.preventDefault(); hide()
      }
    }
    // Native blur already hides in auto-hide mode; flushing also covers a
    // debounce when the writer returns directly to the manuscript.
    const blur = (): void => { void flushPendingWrites().catch(() => setError(true)) }
    const command = (event: MessageEvent): void => {
      if (event.source !== window || event.data?.novalist !== 'menu-command') return
      if (event.data.command === 'app.toggleSceneNotes') hide()
      else if (typeof event.data.command === 'string') window.novalist.forwardMainCommand?.(event.data.command)
    }
    window.addEventListener('keydown', keydown)
    window.addEventListener('blur', blur)
    window.addEventListener('message', command)
    return () => { window.removeEventListener('keydown', keydown); window.removeEventListener('blur', blur); window.removeEventListener('message', command) }
  }, [])

  const valid = context && project.isLoaded && !project.workspaceSuspended &&
    context.projectPath === project.projectPath && context.bookId === project.activeBookId && context.draftId === project.activeDraftId && context.epoch === project.workspaceEpoch &&
    project.chapters.some((chapter) => chapter.guid === context.chapterGuid && chapter.scenes.some((scene) => scene.id === context.sceneId))

  return <>
    <div className="app-shell scene-notes-window" inert={project.workspaceBusy || transferring}>
      <header className="scene-notes-titlebar">
        <span className="scene-notes-title"><ListChecks size={16} />{t('sceneChecklist.title')}</span>
        <button className="scene-notes-control" aria-label={t('sceneChecklist.dock')} title={t('sceneChecklist.dock')}
          onClick={() => void window.novalist.requestSceneNotesDock?.()}><PanelsTopLeft size={16} /></button>
        <button className="scene-notes-control" aria-pressed={windowState.pinned} aria-label={t(windowState.pinned ? 'sceneChecklist.pinned' : 'sceneChecklist.autoHide')}
          title={t(windowState.pinned ? 'sceneChecklist.autoHideHint' : 'sceneChecklist.pinHint')}
          onClick={() => void window.novalist.pinSceneNotes?.(!windowState.pinned)}>
          {windowState.pinned ? <Pin size={16} /> : <PinOff size={16} />}
        </button>
        <button className="scene-notes-control" aria-label={t('sceneChecklist.hide')} title={t('sceneChecklist.hide')} onClick={() => {
          void flushPendingWrites().then(() => window.novalist.hideSceneNotes?.()).catch(() => setError(true))
        }}><EyeOff size={16} /></button>
      </header>
      <label className="scene-notes-follow"><input type="checkbox" checked={windowState.follow} onChange={(event) => {
        const follow = event.target.checked
        setWindowState((state) => ({ ...state, follow }))
        void window.novalist.followSceneNotes?.(follow)
      }} />{t('sceneChecklist.follow')}</label>
      {error && <div className="scene-checklist-error" role="alert">{t('sceneChecklist.saveFailed')}<button className="btn-secondary" onClick={() => {
        void flushPendingWrites().then(hydrateWindow).then(() => { setReady(true); setError(false) }).catch(() => setError(true))
      }}>{t('shell.retry')}</button></div>}
      {!ready ? <p role="status">{t('shell.backendConnecting')}</p> : valid && !released && !windowState.docked ?
        <SceneChecklistView key={JSON.stringify([context.projectPath, context.bookId, context.draftId, context.epoch, context.chapterGuid, context.sceneId])} context={context} /> :
        <p className="scene-checklist-hint">{t('sceneNotes.empty')}</p>}
      <WorkspaceRecoveryDialog />
    </div>
    {closeStage && <WindowClosingDialog stage={closeStage} />}
  </>
}
