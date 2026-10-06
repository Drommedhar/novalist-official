

import { WorkspaceRecoveryDialog } from './WorkspaceRecoveryDialog'

import { CommandPalette } from './CommandPalette'
import { WorkspaceLayoutsDialog } from './WorkspaceLayoutsDialog'
import { FirstRunTour } from './FirstRunTour'
import { QuickOpen } from './QuickOpen'
import { QuickCapture } from './QuickCapture'
import { FindReplaceDialog } from './FindReplaceDialog'
import { CleanupDialog } from './CleanupDialog'
import { HelpOverlay } from './HelpOverlay'

import { ShellDialogs } from './ShellDialogs'

import { SceneConflictDialog } from './SceneConflictDialog'
import { UnsavedLeaveDialog } from './UnsavedLeaveDialog'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore } from '../stores/projectStore'

import { helpTargetForContext, type ManualTarget } from './helpTargets'
import { useSettingsNavigation } from '../views/settings/settingsNavigation'
import { useEditorBridge } from '../stores/editorBridgeStore'
import { MotionPresence } from './MotionPresence'

export function AppShellDialogs(): React.JSX.Element {
  const isLoaded = useProjectStore((s) => s.isLoaded)
  const suspendMotion = useProjectStore((s) => s.closingProject || s.workspaceSuspended)
  const mainView = useShellStore((s) => s.mainView)
  const inspectorTab = useShellStore((s) => s.inspectorTab)
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
  const contextualHelp: ManualTarget =
    isLoaded || mainView === 'settings'
      ? helpTargetForContext({
        view: mainView,
        inspectorTab,
        ...(mainView === 'settings' && settingsSection ? { settingsSection } : {})
      })
      : { file: '01-getting-started.md' }
  return <>
      <MotionPresence disabled={suspendMotion}>{findReplaceOpen && (
        <FindReplaceDialog onClose={() => useShellStore.getState().setFindReplaceOpen(false)} />
      )}</MotionPresence>
      <MotionPresence disabled={suspendMotion}>{cleanupOpen && (
        <CleanupDialog onClose={() => useShellStore.getState().setCleanupOpen(false)} />
      )}</MotionPresence>
      <MotionPresence disabled={suspendMotion}>{commandPaletteOpen && (
        <CommandPalette onClose={() => useShellStore.getState().setCommandPaletteOpen(false)} />
      )}</MotionPresence>
      <MotionPresence disabled={suspendMotion}>{quickOpenOpen && (
        <QuickOpen onClose={() => useShellStore.getState().setQuickOpenOpen(false)} />
      )}</MotionPresence>
      <MotionPresence disabled={suspendMotion}>{quickCaptureOpen && (
        <QuickCapture onClose={() => useShellStore.getState().setQuickCaptureOpen(false)} />
      )}</MotionPresence>
      <MotionPresence disabled={suspendMotion}>{helpOpen && (
        <HelpOverlay
          initialTarget={contextualHelp}
          onClose={() => useShellStore.getState().setHelpOpen(false)}
        />
      )}</MotionPresence>
      <MotionPresence disabled={suspendMotion}>{layoutsOpen && (
        <WorkspaceLayoutsDialog onClose={() => useShellStore.getState().setLayoutsOpen(false)} />
      )}</MotionPresence>
      <MotionPresence disabled={suspendMotion}>{tourOpen && (
        <FirstRunTour
          prerequisites={{ focusPeekAvailable: editorEntityAtCaret }}
          onFocusPeekRequest={() => activeEditor?.peekEntityAtCaret()}
          onClose={() => useShellStore.getState().setTourOpen(false)}
        />
      )}</MotionPresence>
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
  </>
}
