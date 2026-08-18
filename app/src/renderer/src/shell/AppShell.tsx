import { useEffect, useMemo, useRef, useState } from 'react'
import { ModePanel } from './ModePanel'
import { ModeRail } from './ModeRail'
import { Binder } from './Binder'
import { CommandPalette } from './CommandPalette'
import { WorkspaceLayoutsDialog } from './WorkspaceLayoutsDialog'
import { FirstRunTour, hasSeenTour } from './FirstRunTour'
import { QuickOpen } from './QuickOpen'
import { QuickCapture } from './QuickCapture'
import { FindReplaceDialog } from './FindReplaceDialog'
import { CleanupDialog } from './CleanupDialog'
import { HelpOverlay } from './HelpOverlay'
import { runCommand } from './commands'
import { buildDefaultHotkeys, installHotkeys } from './hotkeys'
import { buildMenuLabels, buildMenuTemplate, OPEN_RECENT } from './menuLayout'
import { Inspector } from './Inspector'
import { Toolbar } from './Toolbar'
import { StatusBar } from './StatusBar'
import { MainArea } from './MainArea'
import { MobileShell } from './MobileShell'
import { SceneNotesDock } from './SceneNotesDock'
import { ShellDialogs } from './ShellDialogs'
import { StartScreen } from './StartScreen'
import { UpdateDialog } from './UpdateDialog'
import { useBackupScheduler } from './useBackupScheduler'
import { useSpellCheck } from './useSpellCheck'
import { SceneConflictDialog } from './SceneConflictDialog'
import { UnsavedLeaveDialog } from './UnsavedLeaveDialog'
import { anyPaneShows, useShellStore } from '../stores/shellStore'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { rpc } from '../rpc/client'
import { useExtensionsStore, type StoreUpdate } from '../stores/extensionsStore'
import { useSettingsStore } from '../stores/settingsStore'
import { useUiScaleStore } from '../stores/uiScaleStore'
import { loadUserAssets, watchUserAssets } from '../stores/userAssets'
import type { PingResult } from '../rpc/contract'
import { chromeForView, modeOf } from './modes'
import { helpTargetForContext, type ManualTarget } from './helpTargets'
import { useSettingsNavigation } from '../views/settings/settingsNavigation'
import { useEditorBridge } from '../stores/editorBridgeStore'
import './shell.css'


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
  const binderOverlayOpen = useShellStore((s) => s.binderOverlayOpen)
  const modePanelOpen = useShellStore((s) => s.modePanelOpen)
  const modePanelDocked = useShellStore((s) => s.modePanelDocked)
  const backendVersion = useShellStore((s) => s.backendVersion)
  const focusMode = useShellStore((s) => s.focusMode)
  const inspectorVisible = useShellStore((s) => s.inspectorVisible)
  const inspectorOverlayOpen = useShellStore((s) => s.inspectorOverlayOpen)
  const shellCapacity = useShellStore((s) => s.shellCapacity)
  const mainView = useShellStore((s) => s.mainView)
  const inspectorTab = useShellStore((s) => s.inspectorTab)
  const notesDockVisible = useShellStore((s) => s.notesDockVisible)
  // The surfaces beside the content area belong to the window rather than to
  // one pane, so they ask whether the window holds the view at all - not what
  // the pane the writer last clicked in happens to be showing.
  const editorOpen = useShellStore((s) => anyPaneShows(s.panes, ['write']))
  const sceneContextOpen = useShellStore((s) => anyPaneShows(s.panes, ['write', 'manuscript']))
  const extView = useShellStore((s) => s.extView)
  const isLoaded = useProjectStore((s) => s.isLoaded)
  const recentProjects = useProjectStore((s) => s.recentProjects)
  const openProject = useProjectStore((s) => s.openProject)
  const findReplaceOpen = useShellStore((s) => s.findReplaceOpen)
  const cleanupOpen = useShellStore((s) => s.cleanupOpen)
  const commandPaletteOpen = useShellStore((s) => s.commandPaletteOpen)
  const quickOpenOpen = useShellStore((s) => s.quickOpenOpen)
  const quickCaptureOpen = useShellStore((s) => s.quickCaptureOpen)
  const helpOpen = useShellStore((s) => s.helpOpen)
  const layoutsOpen = useShellStore((s) => s.layoutsOpen)
  const tourOpen = useShellStore((s) => s.tourOpen)
  const settingsSection = useSettingsNavigation((s) => s.destination.section)
  const activeEditor = useEditorBridge((s) => s.editor)
  const editorEntityAtCaret = useEditorBridge((s) => s.entityAtCaret)
  const hotkeys = useMemo(() => buildDefaultHotkeys(), [])
  const shellRef = useRef<HTMLDivElement>(null)
  const chrome = chromeForView(mainView)
  const contextualHelp: ManualTarget =
    isLoaded || mainView === 'settings'
      ? helpTargetForContext({
        view: mainView,
        inspectorTab,
        ...(mainView === 'settings' && settingsSection ? { settingsSection } : {})
      })
      : { file: '01-getting-started.md' }
  // The app is the app from launch. There is no separate front door any more:
  // with no project open the content area holds the welcome material, and
  // whatever needs a project is disabled rather than absent. That is what lets
  // Settings, the manual and About be reachable before anything is opened,
  // instead of Settings having had to learn to open without a project as a
  // special case.
  const showBinder =
    isLoaded &&
    chrome.binder &&
    (shellCapacity === 'compact' ? binderOverlayOpen : binderVisible)
  // The panel lists the views of a mode, so it is shown while the writer is in
  // one. The Dashboard is the screen before you have chosen what to do today
  // and is allowed to talk about all five modes at once; Settings and About
  // belong to no mode at all. Both get the window.
  //
  // Otherwise: docked wherever there is room and the writer wants it, an
  // overlay where there is not, and the same list in the same order either way.
  // Settings, Extensions and About are about the application rather than about
  // a book, which is what lets them open before a project has been - the reason
  // Settings no longer needs its own without-a-project special case.
  const appScopedView =
    mainView === 'settings' || mainView === 'extensions' || mainView === 'about'
  const inAMode = modeOf(mainView) !== null
  const canDock = shellCapacity !== 'compact' && modePanelDocked
  const showModePanel = inAMode && (canDock || modePanelOpen)
  const modePanelOverlay = showModePanel && !canDock
  const showInspector =
    isLoaded &&
    chrome.inspector &&
    sceneContextOpen &&
    (shellCapacity === 'wide' ? inspectorVisible : inspectorOverlayOpen)

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

  // The UI scale is machine-local view state. Apply it before the first useful
  // interaction, and measure the resulting CSS viewport rather than guessing
  // from the physical display.
  useEffect(() => {
    useUiScaleStore.getState().apply()
  }, [])

  useEffect(() => {
    const shell = shellRef.current
    if (!shell) return
    const report = (width: number): void => useShellStore.getState().setShellMetrics(width)
    report(shell.getBoundingClientRect().width)
    const observer = new ResizeObserver((entries) => {
      const width = entries[0]?.contentRect.width
      if (typeof width === 'number') report(width)
    })
    observer.observe(shell)
    return () => observer.disconnect()
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

  // The menu bar is generated from the command registry, so it has to be
  // rebuilt whenever any of its three inputs move: the language its labels are
  // in, whether a project is open (which decides what is greyed out), and the
  // gestures the writer has rebound. Rebuilding is cheap and a stale menu bar
  // is the kind of wrong that looks like a bug in the command rather than in
  // the menu.
  const language = useSettingsStore((s) => s.view?.effective.language)
  const hotkeyBindings = useSettingsStore((s) => s.view?.global.hotkeyBindings)
  useEffect(() => {
    window.novalist.setMenu?.(buildMenuTemplate(), buildMenuLabels())
  }, [isLoaded, language, hotkeyBindings, mainView, recentProjects])

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
        // Every menu item but the updater is a registry command, so the menu
        // bar cannot offer anything the palette does not also have.
        if (data.command === 'help:checkUpdates') void runUpdateCheck(true)
        else if (data.command.startsWith(OPEN_RECENT)) {
          void useProjectStore.getState().openProject(data.command.slice(OPEN_RECENT.length))
        } else runCommand(data.command)
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
    <div
      ref={shellRef}
      className={`shell shell-capacity-${shellCapacity}${isMobile ? ' mobile' : ''}`}
      data-shell-capacity={shellCapacity}
    >
      {/* Composition mode is the whole screen. Leaving the toolbar and the
          status bar in place made it a wider editor rather than a place to
          write. Everything is a keystroke away again. */}
      {!isMobile && !focusMode && <Toolbar />}
      <div className="shell-body">
        {isMobile && isLoaded ? (
          <MobileShell />
        ) : (
          <>
            {!focusMode && <ModeRail />}
            {/* The mode's own views. Docked beside the rail with room for it,
                an overlay when there is not - the same rows either way. */}
            {!focusMode && isLoaded && showModePanel && <ModePanel overlay={modePanelOverlay} />}
            {!focusMode && isLoaded && modePanelOverlay && (
              <div
                className="mode-panel-scrim"
                onPointerDown={() => useShellStore.getState().setModePanelOpen(false)}
              />
            )}
            {showBinder && !focusMode && <Binder />}
            <div className="shell-main">
              {isLoaded || appScopedView ? (
                <MainArea />
              ) : (
                <StartScreen
                  recentProjects={recentProjects}
                  onOpenPath={(path) => void openProject(path)}
                />
              )}
              {editorOpen && !extView && notesDockVisible && !focusMode && <SceneNotesDock />}
            </div>
            {showInspector && !focusMode && !extView && <Inspector />}
          </>
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
      {/* With no project open the status bar is still the only thing that says
          the bundled core process is alive, which is exactly when a writer most
          needs to know. Once one is open, the mode decides. */}
      {!isMobile && !focusMode && (!isLoaded || chrome.status) && <StatusBar />}
      {findReplaceOpen && (
        <FindReplaceDialog onClose={() => useShellStore.getState().setFindReplaceOpen(false)} />
      )}
      {cleanupOpen && (
        <CleanupDialog onClose={() => useShellStore.getState().setCleanupOpen(false)} />
      )}
      {commandPaletteOpen && (
        <CommandPalette onClose={() => useShellStore.getState().setCommandPaletteOpen(false)} />
      )}
      {quickOpenOpen && (
        <QuickOpen onClose={() => useShellStore.getState().setQuickOpenOpen(false)} />
      )}
      {quickCaptureOpen && (
        <QuickCapture onClose={() => useShellStore.getState().setQuickCaptureOpen(false)} />
      )}
      {helpOpen && (
        <HelpOverlay
          initialTarget={contextualHelp}
          onClose={() => useShellStore.getState().setHelpOpen(false)}
        />
      )}
      {layoutsOpen && (
        <WorkspaceLayoutsDialog onClose={() => useShellStore.getState().setLayoutsOpen(false)} />
      )}
      {tourOpen && (
        <FirstRunTour
          prerequisites={{ focusPeekAvailable: editorEntityAtCaret }}
          onFocusPeekRequest={() => activeEditor?.peekEntityAtCaret()}
          onClose={() => useShellStore.getState().setTourOpen(false)}
        />
      )}
      {/* Every dialog a command can name, in one place, so the palette and the
          menu bar raise the same ones the toolbar does. */}
      <ShellDialogs />
      {/* Raised by the store when a save was refused because the scene changed
          on disk. Renders nothing until there is something to resolve. */}
      <SceneConflictDialog />
      {/* Raised by the store when a navigation would leave unsaved edits
          behind, wherever the writer set off from. */}
      <UnsavedLeaveDialog />
    </div>
  )
}
