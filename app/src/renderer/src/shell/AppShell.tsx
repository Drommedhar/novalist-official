import { WorkspaceRecoveryDialog } from './WorkspaceRecoveryDialog'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
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
import { buildDefaultHotkeys, installHotkeys, setHotkeysEnabled } from './hotkeys'
import { buildMenuLabels, buildMenuTemplate, OPEN_RECENT } from './menuLayout'
import { Inspector } from './Inspector'
import { Toolbar } from './Toolbar'
import { StatusBar } from './StatusBar'
import { MainArea } from './MainArea'
import { MobileShell } from './MobileShell'
import { MobileWelcome } from './MobileWelcome'
import { SceneNotesDock } from './SceneNotesDock'
import { ShellDialogs } from './ShellDialogs'
import { StartScreen } from './ProjectLibrary'
import { FocusMode } from './FocusMode'
import { UpdateDialog } from './UpdateDialog'
import {
  clearCloseBackupHandledForQuit,
  createCloseBackup,
  markCloseBackupHandledForQuit,
  useBackupScheduler
} from './useBackupScheduler'
import { useSpellCheck } from './useSpellCheck'
import { SceneConflictDialog } from './SceneConflictDialog'
import { UnsavedLeaveDialog } from './UnsavedLeaveDialog'
import { BINDER_MIN, BINDER_MAX, panelWidthForShell, anyPaneShows, useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'
import { rpc } from '../rpc/client'
import { useExtensionsStore, type StoreUpdate } from '../stores/extensionsStore'
import { useSettingsStore } from '../stores/settingsStore'
import { useUiScaleStore } from '../stores/uiScaleStore'
import { chromeForView, modeOf } from './modes'
import { helpTargetForContext, type ManualTarget } from './helpTargets'
import { useSettingsNavigation } from '../views/settings/settingsNavigation'
import { useEditorBridge } from '../stores/editorBridgeStore'
import { flushWindowWrites, useWorkspaceWindow } from './useWorkspaceWindow'
import { hydrateWindow } from './bootstrap'
import './shell.css'
import '../styles/desktop.css'
import '../styles/desktop-views.css'


export function AppShell(): React.JSX.Element {
  const { t } = useTranslation()
  useWorkspaceWindow()
  useBackupScheduler()
  useSpellCheck()
  const binderVisible = useShellStore((s) => s.binderVisible)
  const binderOverlayOpen = useShellStore((s) => s.binderOverlayOpen)
  const binderWidth = useShellStore((s) => panelWidthForShell(s.binderWidth, s.shellWidth, BINDER_MIN, BINDER_MAX))
  const modePanelOpen = useShellStore((s) => s.modePanelOpen)
  const modePanelDocked = useShellStore((s) => s.modePanelDocked)
  const backendVersion = useShellStore((s) => s.backendVersion)
  useEffect(() => {
    if (!backendVersion) return
    // Projects can be removed in the file manager while this window is open.
    let refreshTimer: ReturnType<typeof setTimeout> | undefined
    const refresh = (): void => {
      // Browser focus and visibility commonly arrive together. Give the
      // interaction priority and coalesce them into one background refresh.
      clearTimeout(refreshTimer)
      refreshTimer = setTimeout(() => {
        void useProjectStore.getState().loadRecents().catch(() => {})
      }, 250)
    }
    const onVisible = (): void => { if (document.visibilityState === 'visible') refresh() }
    window.addEventListener('focus', refresh)
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      clearTimeout(refreshTimer)
      window.removeEventListener('focus', refresh)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [backendVersion])
  const focusMode = useShellStore((s) => s.focusMode)
  const focusToolsVisible = useShellStore((s) => s.focusToolsVisible)
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
  const closingProject = useProjectStore((s) => s.closingProject)
  const changingSceneStructure = useProjectStore((s) => s.changingSceneStructure)
  const workspaceBusy = useProjectStore((s) => s.workspaceBusy)
  const workspaceSuspended = useProjectStore((s) => s.workspaceSuspended)
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
  const combinedSidebar = showModePanel && !modePanelOverlay && showBinder
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
  const updateOpenRef = useRef(false)
  useEffect(() => { updateOpenRef.current = updateOpen }, [updateOpen])
  const [updateProgress, setUpdateProgress] = useState<number | null>(null)
  const [downloading, setDownloading] = useState(false)
  const installingRef = useRef(false)
  const transferRef = useRef(false)
  const launchTokenRef = useRef<string | null>(null)
  const checkingRef = useRef(false)
  const [installing, setInstalling] = useState(false)
  const [ready, setReady] = useState(false)
  const [manualInstall, setManualInstall] = useState(false)
  const [checking, setChecking] = useState(false)
  const [updateNotice, setUpdateNotice] = useState(false)
  const [appVersion, setAppVersion] = useState<string | null>(null)
  useEffect(() => { void window.novalist.appVersion?.().then(setAppVersion).catch(() => {}) }, [])
  const [updateError, setUpdateError] = useState<{ keys: string[] } | { message: string } | null>(null)
  const updateErrorMessage = updateError && ('keys' in updateError
    ? updateError.keys.map((key) => t(key)).join(' ')
    : updateError.message)

  useEffect(() => {
    installingRef.current = installing
  }, [installing])

  const runUpdateCheck = async (manual: boolean): Promise<void> => {
    if (manual) setUpdateOpen(true)
    if (checkingRef.current || transferRef.current || launchTokenRef.current || installingRef.current) return
    checkingRef.current = true
    setManualInstall(false)
    setChecking(true)
    setUpdateError(null)
    const errors: string[] = []
    let app: AppUpdate | null = null
    let ext: StoreUpdate[] = []
    try { app = await window.novalist.checkAppUpdate() }
    catch { errors.push('desktopRefresh.checkAppFailed') }
    try {
      await useExtensionsStore.getState().checkStoreUpdates()
      ext = useExtensionsStore.getState().storeUpdates
    } catch { errors.push('desktopRefresh.checkExtensionsFailed') }
    setAppUpdate(app)
    setExtUpdates(ext)
    setUpdateError(errors.length ? { keys: errors } : null)
    setChecking(false)
    checkingRef.current = false
    if (app || ext.length) setUpdateNotice(true)
  }

  const updateExtension = async (u: StoreUpdate): Promise<void> => {
    setUpdateError(null)
    setUpdatingExtId(u.extensionId)
    try {
      const result = await useExtensionsStore.getState().installFromStore(u.extensionId, u.repo, true)
      if (!result.success) throw new Error(t('desktopRefresh.extensionFailed'))
      // installFromStore drops the entry from storeUpdates on success.
      setExtUpdates(useExtensionsStore.getState().storeUpdates)
    } catch {
      setUpdateError({ keys: ['desktopRefresh.extensionFailed'] })
    } finally {
      setUpdatingExtId(null)
    }
  }

  useEffect(() => {
    rpc.onReconnected(() => void hydrateWindow().catch(() => {}))
    // Boot: connect, hydrate, run the combined update check, then tell main the
    // check finished so it can close the splash (updatesChecked always fires).
    void rpc
      .connect()
      .then(hydrateWindow)
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
      if (installingRef.current || useProjectStore.getState().workspaceBusy || useProjectStore.getState().changingSceneStructure) return
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

  useEffect(() => {
    setHotkeysEnabled(!installing && !workspaceBusy && !updateOpen && !changingSceneStructure)
    if (!installing && !workspaceBusy && !updateOpen && !changingSceneStructure) return installHotkeys(hotkeys)
  }, [hotkeys, installing, workspaceBusy, updateOpen, changingSceneStructure])

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
      if (event.source !== window) return
      const data = event.data as { novalist?: string; command?: string; percent?: number }
      if (data?.novalist === 'update-progress' && typeof data.percent === 'number')
        setUpdateProgress(data.percent)
      if (data?.novalist === 'menu-command' && data.command) {
        // The update dialog is deliberately non-dismissible during handoff;
        // native menu commands must respect the same lock.
        if (installingRef.current || useProjectStore.getState().workspaceBusy || useProjectStore.getState().changingSceneStructure || (updateOpenRef.current && data.command !== 'help:checkUpdates')) return
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
    if (!appUpdate || transferRef.current || launchTokenRef.current) return
    transferRef.current = true
    setDownloading(true)
    setUpdateProgress(0)
    setUpdateError(null)
    setUpdateNotice(true)
    try {
      const result = await window.novalist.downloadAppUpdate(appUpdate)
      launchTokenRef.current = result.launchToken ?? null
      setReady(Boolean(result.launchToken))
      setManualInstall(!result.launchToken)
    } catch (error) {
      setUpdateError({ message: error instanceof Error ? error.message : String(error) })
    } finally {
      transferRef.current = false
      setDownloading(false)
    }
  }

  const installAppUpdate = async (): Promise<void> => {
    const token = launchTokenRef.current
    if (!token || installingRef.current) return
    installingRef.current = true
    setInstalling(true)
    setUpdateError(null)
    try {
      // Writing continues during download. Flush again after the explicit
      // install action, and once more after the close backup.
      await flushWindowWrites()
      await createCloseBackup()
      await flushWindowWrites()
      markCloseBackupHandledForQuit()
      try {
        // The token-authenticated main-process call launches and quits as one
        // operation, so a Linux helper cannot be left waiting on a renderer
        // that never managed to send a separate quit acknowledgement.
        await window.novalist.launchAppUpdate(token)
      } catch (error) {
        launchTokenRef.current = null
        setReady(false)
        clearCloseBackupHandledForQuit()
        throw error
      }

    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      setUpdateError({ message })
    } finally {
      installingRef.current = false
      setInstalling(false)
    }
  }


  const isMobile = window.novalist.isMobile === true
  const focused = focusMode && isLoaded && mainView === 'write' && !extView && !isMobile

  return (
    <>
    <div
      ref={shellRef}
      inert={closingProject || workspaceBusy || changingSceneStructure}
      className={`shell shell-capacity-${shellCapacity}${isMobile ? ' mobile' : ''}${focused ? ' shell-focus' : ''}${focused && focusToolsVisible ? ' focus-tools-open' : ''}`}
      data-shell-capacity={shellCapacity}
    >
      {/* Composition mode is the whole screen. Leaving the toolbar and the
          status bar in place made it a wider editor rather than a place to
          write. Everything is a keystroke away again. */}
      {!isMobile && !focused && <Toolbar />}
      {!isMobile && !focused && <ModeRail />}
      {!isMobile && !focused && updateNotice && (appUpdate || extUpdates.length > 0) && <div className="update-banner" role="status">
        <span>{downloading ? t('desktopRefresh.backgroundDownload', { percent: updateProgress ?? 0 }) : ready ? t('desktopRefresh.ready') : t('desktopRefresh.available')}</span>
        <button className="dialog-button" onClick={() => setUpdateOpen(true)}>{t('desktopRefresh.reviewUpdate')}</button>
        <button className="dialog-button" onClick={() => setUpdateNotice(false)}>{t('desktopRefresh.dismiss')}</button>
      </div>}
      <div className="shell-body" inert={installing}>
        {isMobile ? (
          isLoaded ? (
            <MobileShell />
          ) : !workspaceSuspended && !closingProject ? (
            /* No project yet. The desktop shell's welcome content, in the
               mobile frame - the rail and the panes it sits beside are not
               phone controls. */
            <MobileWelcome
              recentProjects={recentProjects}
              onOpenPath={(path, bookId) => openProject(path, bookId)}
            />
          ) : null
        ) : (
          <>
            {/* The mode's views share a column with the binder when docked,
                an overlay when there is not - the same rows either way. */}
            <div className={combinedSidebar ? 'workspace-sidebar' : 'workspace-navigation'} style={combinedSidebar ? { width: binderWidth } : undefined}>
              {!focused && isLoaded && showModePanel && <ModePanel overlay={modePanelOverlay} />}
              {showBinder && !focused && <Binder />}
            </div>
            {!focused && isLoaded && modePanelOverlay && (
              <div
                className="mode-panel-scrim"
                onPointerDown={() => useShellStore.getState().setModePanelOpen(false)}
              />
            )}
            <div className="shell-main">
              {isLoaded || appScopedView ? (
                <MainArea />
              ) : !workspaceSuspended && !closingProject ? (
                <StartScreen
                  recentProjects={recentProjects}
                  onOpenPath={(path, bookId) => openProject(path, bookId)}
                />
              ) : null}
              {!workspaceSuspended && editorOpen && !extView && notesDockVisible && !(showInspector && inspectorTab === 'notes') && !focused && <SceneNotesDock />}
            </div>
            {!workspaceSuspended && showInspector && !focused && !extView && <Inspector />}
          </>
        )}
      </div>
      {!workspaceSuspended && !isMobile && isLoaded && focusMode && <FocusMode />}
      {updateOpen && (
        <UpdateDialog
          appUpdate={appUpdate}
          currentVersion={appVersion}
          extUpdates={extUpdates}
          updatingExtId={updatingExtId}
          progress={updateProgress}
          downloading={downloading}
          installing={installing}
          ready={ready}
          manualInstall={manualInstall}
          checking={checking}
          onCheck={() => void runUpdateCheck(true)}
          onInstall={() => void installAppUpdate()}
          error={updateErrorMessage}
          onDownload={() => void downloadAppUpdate()}
          onUpdateExt={(u) => void updateExtension(u)}
          onClose={() => {
            setUpdateOpen(false)
          }}
        />
      )}
      {/* With no project open the status bar is still the only thing that says
          the bundled core process is alive, which is exactly when a writer most
          needs to know. Once one is open, the mode decides. */}
      {!isMobile && !focused && (!isLoaded || chrome.status) && <StatusBar />}
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
      <WorkspaceRecoveryDialog />
      <SceneConflictDialog />
      {/* Raised by the store when a navigation would leave unsaved edits
          behind, wherever the writer set off from. */}
      <UnsavedLeaveDialog />
    </div>
    {closingProject && <div className="dialog-overlay" role="status" aria-live="polite">
      <div className="dialog-card"><p>{t('bookshelf.closingProject')}</p></div>
    </div>}
    </>
  )
}
