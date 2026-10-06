import { AppShellDialogs } from './AppShellDialogs'
import { AppUpdateOverlay } from './AppUpdateOverlay'
import { useRecentProjects, useShellStartup, useShellSize, useDeepLinks, useProjectLanding } from './shellLifecycle'
import { useShellCommands } from './useShellCommands'
import { useAppUpdateState } from './appUpdateState'

import { WindowClosingDialog } from './WindowClosingDialog'
import { useLayoutEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { ModePanel } from './ModePanel'
import { ModeRail } from './ModeRail'
import { Binder } from './Binder'
import { MotionPresence } from './MotionPresence'

import { Inspector } from './Inspector'
import { Toolbar } from './Toolbar'
import { StatusBar } from './StatusBar'
import { MainArea } from './MainArea'
import { MobileShell } from './MobileShell'
import { MobileWelcome } from './MobileWelcome'
import { SceneNotesDock } from './SceneNotesDock'

import { StartScreen } from './ProjectLibrary'
import { FocusMode } from './FocusMode'

import { useBackupScheduler } from './useBackupScheduler'
import { useSpellCheck } from './useSpellCheck'

import { BINDER_MIN, BINDER_MAX, panelWidthForShell, anyPaneShows, useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'

import { chromeForView, modeOf } from './modes'

import { useWorkspaceWindow } from './useWorkspaceWindow'

import './shell.css'
import '../styles/desktop.css'
import '../styles/desktop-views.css'

export function AppShell(): React.JSX.Element {
  const { t } = useTranslation()
  const closeStage = useWorkspaceWindow()
  useBackupScheduler()
  useSpellCheck()
  const binderVisible = useShellStore((s) => s.binderVisible)
  const binderOverlayOpen = useShellStore((s) => s.binderOverlayOpen)
  const binderWidth = useShellStore((s) => panelWidthForShell(s.binderWidth, s.shellWidth, BINDER_MIN, BINDER_MAX))
  const modePanelOpen = useShellStore((s) => s.modePanelOpen)
  const modePanelDocked = useShellStore((s) => s.modePanelDocked)
  useRecentProjects()

  const focusMode = useShellStore((s) => s.focusMode)
  const focusToolsVisible = useShellStore((s) => s.focusToolsVisible)
  const inspectorVisible = useShellStore((s) => s.inspectorVisible)
  const inspectorOverlayOpen = useShellStore((s) => s.inspectorOverlayOpen)
  const shellCapacity = useShellStore((s) => s.shellCapacity)
  const previousCapacity = useRef(shellCapacity)
  const capacityChanging = previousCapacity.current !== shellCapacity
  useLayoutEffect(() => { previousCapacity.current = shellCapacity }, [shellCapacity])
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
  const shellRef = useRef<HTMLDivElement>(null)
  const chrome = chromeForView(mainView)
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

  const updates = useAppUpdateState()
  const { appUpdate, extUpdates, setUpdateOpen, updateProgress, downloading, installing, ready, updateNotice, setUpdateNotice } = updates
  useShellStartup(updates)

  useShellSize(shellRef)

  useDeepLinks(updates)

  useProjectLanding()

  useShellCommands(updates)

  const isMobile = window.novalist.isMobile === true
  const focused = focusMode && isLoaded && mainView === 'write' && !extView && !isMobile
  const disablePanelMotion = capacityChanging || focused || closingProject || workspaceSuspended || workspaceBusy || changingSceneStructure

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
            <div className={combinedSidebar ? 'workspace-sidebar' : 'workspace-navigation'} style={{ width: binderWidth }}>
              <MotionPresence disabled={disablePanelMotion}>
                {!focused && isLoaded && showModePanel && <ModePanel overlay={modePanelOverlay} />}
              </MotionPresence>
              <MotionPresence disabled={disablePanelMotion}>
                {showBinder && !focused && <Binder />}
              </MotionPresence>
            </div>
            <MotionPresence disabled={disablePanelMotion}>
              {!focused && isLoaded && modePanelOverlay && (
                <div
                  className="mode-panel-scrim"
                  onPointerDown={() => useShellStore.getState().setModePanelOpen(false)}
                />
              )}
            </MotionPresence>
            <div className="shell-main">
              {isLoaded || appScopedView ? (
                <MainArea />
              ) : !workspaceSuspended && !closingProject ? (
                <StartScreen
                  recentProjects={recentProjects}
                  onOpenPath={(path, bookId) => openProject(path, bookId)}
                />
              ) : null}
              <MotionPresence disabled={disablePanelMotion}>
                {!workspaceSuspended && editorOpen && !extView && notesDockVisible && !(showInspector && inspectorTab === 'notes') && !focused && <SceneNotesDock />}
              </MotionPresence>
            </div>
            <MotionPresence disabled={disablePanelMotion}>
              {!workspaceSuspended && showInspector && !focused && !extView && <Inspector />}
            </MotionPresence>
          </>
        )}
      </div>
      {!workspaceSuspended && !isMobile && isLoaded && focusMode && <FocusMode />}
      <AppUpdateOverlay updates={updates} />
      {/* With no project open the status bar is still the only thing that says
          the bundled core process is alive, which is exactly when a writer most
          needs to know. Once one is open, the mode decides. */}
      {!isMobile && !focused && (!isLoaded || chrome.status) && <StatusBar />}
      <AppShellDialogs />
    </div>
    {closeStage && <WindowClosingDialog stage={closeStage} />}
    {closingProject && <div className="dialog-overlay" role="status" aria-live="polite">
      <div className="dialog-card"><p>{t('bookshelf.closingProject')}</p></div>
    </div>}
    </>
  )
}
