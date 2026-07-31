import { useEffect, useMemo, useState } from 'react'
import { ActivityBar } from './ActivityBar'
import { Binder } from './Binder'
import { CommandPalette } from './CommandPalette'
import { WorkspaceLayoutsDialog } from './WorkspaceLayoutsDialog'
import { FirstRunTour, hasSeenTour } from './FirstRunTour'
import { QuickOpen } from './QuickOpen'
import { QuickCapture } from './QuickCapture'
import { FindReplaceDialog } from './FindReplaceDialog'
import { CleanupDialog } from './CleanupDialog'
import { HelpOverlay } from './HelpOverlay'
import { buildDefaultHotkeys, installHotkeys } from './hotkeys'
import { Inspector } from './Inspector'
import { Toolbar } from './Toolbar'
import { StatusBar } from './StatusBar'
import { MainArea } from './MainArea'
import { MobileShell } from './MobileShell'
import { SceneNotesDock } from './SceneNotesDock'
import { StartScreen } from './StartScreen'
import { UpdateDialog } from './UpdateDialog'
import { useBackupScheduler } from './useBackupScheduler'
import { useSpellCheck } from './useSpellCheck'
import { SceneConflictDialog } from './SceneConflictDialog'
import { useShellStore, type MainView } from '../stores/shellStore'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { rpc } from '../rpc/client'
import { useExtensionsStore, type StoreUpdate } from '../stores/extensionsStore'
import { useSettingsStore } from '../stores/settingsStore'
import { loadUserAssets, watchUserAssets } from '../stores/userAssets'
import type { PingResult } from '../rpc/contract'
import './shell.css'

/** Maps a native-menu command ("nav:codex", "toggle:focus") onto the shell store. */
function handleMenuCommand(command: string): void {
  const shell = useShellStore.getState()
  if (command.startsWith('nav:')) {
    shell.setMainView(command.slice(4) as MainView)
    return
  }
  switch (command) {
    case 'toggle:binder':
      shell.toggleBinder()
      break
    case 'toggle:inspector':
      shell.toggleInspector()
      break
    case 'toggle:sceneNotes':
      shell.toggleNotesDock()
      break
    case 'toggle:focus':
      shell.toggleFocusMode()
      break
    case 'help:manual':
      shell.setHelpOpen(true)
      break
  }
}

async function hydrate(): Promise<void> {
  const ping = await rpc.request<PingResult>('system/ping')
  useShellStore.getState().setBackendVersion(ping.version)
  // User themes and locales first: settings may name one of them, and a theme
  // or language that registers afterwards would apply a frame too late.
  // Registered before the first load so an edit landing during startup is
  // still picked up.
  watchUserAssets()
  await loadUserAssets()
  // Apply the user's settings (language, theme, gestures) at startup - not just
  // when the Settings view is first opened - so the app isn't stuck on the OS
  // language / default theme until then.
  await useSettingsStore.getState().load()
  const state = await rpc.request<ProjectStateDto>('project/getState')
  useProjectStore.getState().applyState(state)
  await useProjectStore.getState().loadRecents()
  await useExtensionsStore.getState().load()
}

export function AppShell(): React.JSX.Element {
  useBackupScheduler()
  useSpellCheck()
  const binderVisible = useShellStore((s) => s.binderVisible)
  const backendVersion = useShellStore((s) => s.backendVersion)
  const focusMode = useShellStore((s) => s.focusMode)
  const inspectorVisible = useShellStore((s) => s.inspectorVisible)
  const notesDockVisible = useShellStore((s) => s.notesDockVisible)
  const mainView = useShellStore((s) => s.mainView)
  const extView = useShellStore((s) => s.extView)
  const isLoaded = useProjectStore((s) => s.isLoaded)
  const recentProjects = useProjectStore((s) => s.recentProjects)
  const openProject = useProjectStore((s) => s.openProject)
  const pickAndOpenProject = useProjectStore((s) => s.pickAndOpenProject)
  const findReplaceOpen = useShellStore((s) => s.findReplaceOpen)
  const cleanupOpen = useShellStore((s) => s.cleanupOpen)
  const commandPaletteOpen = useShellStore((s) => s.commandPaletteOpen)
  const quickOpenOpen = useShellStore((s) => s.quickOpenOpen)
  const quickCaptureOpen = useShellStore((s) => s.quickCaptureOpen)
  const helpOpen = useShellStore((s) => s.helpOpen)
  const layoutsOpen = useShellStore((s) => s.layoutsOpen)
  const tourOpen = useShellStore((s) => s.tourOpen)
  const hotkeys = useMemo(() => buildDefaultHotkeys(), [])

  // ── Combined app + extension update check (run in the splash on startup) ──
  const [appUpdate, setAppUpdate] = useState<AppUpdate | null>(null)
  const [extUpdates, setExtUpdates] = useState<StoreUpdate[]>([])
  const [updatingExtId, setUpdatingExtId] = useState<string | null>(null)
  const [updateOpen, setUpdateOpen] = useState(false)
  const [updateProgress, setUpdateProgress] = useState<number | null>(null)
  const [downloading, setDownloading] = useState(false)

  const runUpdateCheck = async (manual: boolean): Promise<void> => {
    let app: AppUpdate | null = null
    let ext: StoreUpdate[] = []
    try {
      app = (await window.novalist.checkAppUpdate()) as AppUpdate | null
    } catch {
      /* offline / no release metadata — leave app update absent */
    }
    try {
      await useExtensionsStore.getState().checkStoreUpdates()
      ext = useExtensionsStore.getState().storeUpdates
    } catch {
      /* offline — leave extension updates empty */
    }
    setAppUpdate(app)
    setExtUpdates(ext)
    if (app || ext.length > 0 || manual) setUpdateOpen(true)
  }

  const updateExtension = async (u: StoreUpdate): Promise<void> => {
    setUpdatingExtId(u.extensionId)
    try {
      await useExtensionsStore.getState().installFromStore(u.extensionId, u.repo, true)
      // installFromStore drops the entry from storeUpdates on success.
      setExtUpdates(useExtensionsStore.getState().storeUpdates)
    } catch {
      /* leave the row so the user can retry */
    } finally {
      setUpdatingExtId(null)
    }
  }

  useEffect(() => {
    rpc.onReconnected(() => void hydrate())
    // Boot: connect, hydrate, run the combined update check, then tell main the
    // check finished so it can close the splash (updatesChecked always fires).
    void rpc
      .connect()
      .then(hydrate)
      // Extension scripts that run inside the interface. After connecting, not
      // at module load: this makes the first request of the session, and at
      // import time there is no connection for it to make it on.
      .then(async () => {
        const { loadRendererPlugins } = await import('./pluginHost')
        await loadRendererPlugins()
      })
      .then(() => (window.novalist.autoUpdate ? runUpdateCheck(false) : undefined))
      .then(async () => {
        // A novalist:// link that started the app has been waiting since before
        // the renderer existed.
        const waiting = await window.novalist.takeDeepLink()
        if (!waiting) return
        await useProjectStore.getState().openProject(waiting.project)
        if (waiting.chapter && waiting.scene) {
          await useProjectStore.getState().openScene(waiting.chapter, waiting.scene)
        }
      })
      .catch(() => {})
      .finally(() => window.novalist.updatesChecked())
  }, [])

  /**
   * novalist:// links, so something outside the app can point at a place in it.
   *
   * Both directions: one waiting from launch, which is the usual case because a
   * link is normally what starts the app, and any that arrive while it is
   * already open.
   */
  useEffect(() => {
    const follow = async (link: {
      project: string
      chapter?: string
      scene?: string
    }): Promise<void> => {
      try {
        await useProjectStore.getState().openProject(link.project)
        // A scene id means nothing without the chapter that holds it, so the
        // link carries both or neither.
        if (link.chapter && link.scene) {
          await useProjectStore.getState().openScene(link.chapter, link.scene)
        }
      } catch {
        // A link to a project that has moved should do nothing rather than
        // leave the app half-open on it.
      }
    }

    window.novalist.onDeepLink((link) => void follow(link))
  }, [])

  // Opening a project lands on the dashboard, matching the Avalonia app.
  useEffect(() => {
    if (isLoaded) useShellStore.getState().setMainView('dashboard')
  }, [isLoaded])

  // The tour is offered once, and only with a project open: every stop switches
  // to a real view, and a walk through eighteen empty ones teaches nothing.
  useEffect(() => {
    if (isLoaded && !hasSeenTour()) useShellStore.getState().setTourOpen(true)
  }, [isLoaded])

  useEffect(() => installHotkeys(hotkeys), [hotkeys])

  // Mobile: the native Liquid Glass tab bar shows only inside a project.
  useEffect(() => {
    if (window.novalist.isMobile) window.novalist.setNavVisible?.(isLoaded)
  }, [isLoaded])

  useEffect(() => {
    const onMessage = (event: MessageEvent): void => {
      const data = event.data as { novalist?: string; command?: string; percent?: number }
      if (data?.novalist === 'update-progress' && typeof data.percent === 'number')
        setUpdateProgress(data.percent)
      if (data?.novalist === 'menu-command' && data.command) {
        if (data.command === 'help:checkUpdates') void runUpdateCheck(true)
        else handleMenuCommand(data.command)
      }
    }
    window.addEventListener('message', onMessage)
    return () => window.removeEventListener('message', onMessage)
  }, [])

  const downloadAppUpdate = async (): Promise<void> => {
    if (!appUpdate) return
    setDownloading(true)
    setUpdateProgress(0)
    try {
      await window.novalist.downloadAppUpdate(appUpdate)
    } finally {
      setDownloading(false)
    }
  }


  const isMobile = window.novalist.isMobile === true

  return (
    <div className={`shell${isMobile ? ' mobile' : ''}`}>
      {/* Composition mode is the whole screen. Leaving the toolbar and the
          status bar in place made it a wider editor rather than a place to
          write. Everything is a keystroke away again. */}
      {!isMobile && !focusMode && <Toolbar />}
      <div className="shell-body">
        {isLoaded ? (
          isMobile ? (
            <MobileShell />
          ) : (
            <>
              {!focusMode && <ActivityBar />}
              {binderVisible && !focusMode && <Binder />}
              <div className="shell-main">
                <MainArea />
                {mainView === 'write' && !extView && notesDockVisible && !focusMode && (
                  <SceneNotesDock />
                )}
              </div>
              {inspectorVisible &&
                !focusMode &&
                !extView &&
                (mainView === 'write' || mainView === 'manuscript') && <Inspector />}
            </>
          )
        ) : (
          <StartScreen
            recentProjects={recentProjects}
            onOpenPath={(path) => void openProject(path)}
            onPickProject={() => void pickAndOpenProject()}
          />
        )}
      </div>
      {updateOpen && (
        <UpdateDialog
          appUpdate={appUpdate}
          currentVersion={backendVersion}
          extUpdates={extUpdates}
          updatingExtId={updatingExtId}
          progress={updateProgress}
          downloading={downloading}
          onDownload={() => void downloadAppUpdate()}
          onUpdateExt={(u) => void updateExtension(u)}
          onClose={() => {
            setUpdateOpen(false)
            setUpdateProgress(null)
          }}
        />
      )}
      {!isMobile && !focusMode && <StatusBar />}
      {findReplaceOpen && (
        <FindReplaceDialog onClose={() => useShellStore.getState().setFindReplaceOpen(false)} />
      )}
      {cleanupOpen && (
        <CleanupDialog onClose={() => useShellStore.getState().setCleanupOpen(false)} />
      )}
      {commandPaletteOpen && (
        <CommandPalette
          actions={hotkeys}
          onClose={() => useShellStore.getState().setCommandPaletteOpen(false)}
        />
      )}
      {quickOpenOpen && (
        <QuickOpen onClose={() => useShellStore.getState().setQuickOpenOpen(false)} />
      )}
      {quickCaptureOpen && (
        <QuickCapture onClose={() => useShellStore.getState().setQuickCaptureOpen(false)} />
      )}
      {helpOpen && <HelpOverlay onClose={() => useShellStore.getState().setHelpOpen(false)} />}
      {layoutsOpen && (
        <WorkspaceLayoutsDialog onClose={() => useShellStore.getState().setLayoutsOpen(false)} />
      )}
      {tourOpen && <FirstRunTour onClose={() => useShellStore.getState().setTourOpen(false)} />}
      {/* Raised by the store when a save was refused because the scene changed
          on disk. Renders nothing until there is something to resolve. */}
      <SceneConflictDialog />
    </div>
  )
}
