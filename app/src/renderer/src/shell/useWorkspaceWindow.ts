import { useEffect, useState } from 'react'
import { flushSync } from 'react-dom'
import i18n from '../i18n'
import { rpc } from '../rpc/client'
import { capturePendingWrites, flushPendingWrites } from '../stores/pendingWrites'
import { prepareProjectLibrary, reportEditingScenes, useProjectStore } from '../stores/projectStore'
import { hasPendingManuscriptWrites, resetManuscriptWorkspace, useManuscriptStore } from '../stores/manuscriptStore'
import { useCodexStore } from '../stores/codexStore'
import { useSettingsStore } from '../stores/settingsStore'
import { useHostBridgeStore } from '../stores/hostBridgeStore'
import { hasWorkspaceDialogDraft } from './useWorkspaceDialogGuard'
import { clearCloseBackupHandledForQuit, createCloseBackup, markCloseBackupHandledForQuit } from './useBackupScheduler'
import { setHotkeysEnabled } from './hotkeys'

export type WindowCloseStage = 'flush' | 'backup'

/** Every window drains its own surfaces; the main process owns the global
 * transaction and does not let one successful window stand in for another. */
export async function flushWindowWrites(): Promise<void> {
  let timer: ReturnType<typeof setTimeout> | undefined
  try {
    await Promise.race([
      drainWindowWrites(),
      new Promise<never>((_resolve, reject) => {
        timer = setTimeout(() => reject(new Error(i18n.t('update.workspaceBusy'))), 10_000)
      })
    ])
  } finally { clearTimeout(timer) }
}

async function drainWindowWrites(): Promise<void> {
  await flushPendingWrites()
  await useProjectStore.getState().flushPendingSave()
  await useManuscriptStore.getState().flushPendingSave()
  await rpc.request('system/barrier')
  const project = useProjectStore.getState()
  if (project.sceneConflict || project.isDirty || Object.values(project.dirtyMap).some(Boolean)) {
    throw new Error(i18n.t('update.unsavedConflict'))
  }
}

export async function loadWorkspaceSnapshot(): Promise<void> {
  const snapshot = await window.novalist.workspaceSnapshot?.()
  if (!snapshot) return
  const previous = useProjectStore.getState()
  if (previous.workspaceTransitionToken || (previous.workspaceBusy && !previous.workspaceRecovering)) return
  const sameScope = previous.projectPath === snapshot.state.projectPath && previous.activeBookId === snapshot.state.activeBookId && previous.activeDraftId === snapshot.draftId
  if (previous.workspaceRecovering && !sameScope && (previous.isDirty || Object.values(previous.dirtyMap).some(Boolean) || hasPendingManuscriptWrites())) {
    const message = 'The previous workspace could not be restored. Your pending edits remain in their windows.'
    useProjectStore.setState({ workspaceRecoveryError: message })
    throw new Error(message)
  }
  useProjectStore.getState().applyWorkspaceSnapshot(snapshot)
  if (!sameScope) resetManuscriptWorkspace()
  if (previous.workspaceRecovering) {
    try {
      await useProjectStore.getState().refreshWorkspaceEditors(true)
      if (!snapshot.state.isLoaded) await prepareProjectLibrary()
    }
    catch (error) {
      useProjectStore.setState({ workspaceRecoveryError: String(error) })
      throw error
    }
    const current = useProjectStore.getState()
    if (current.workspaceTransitionToken || current.workspaceEpoch !== snapshot.epoch || !current.workspaceRecovering) return
    useProjectStore.setState({ workspaceRecovering: false, workspaceRecoveryError: null, workspaceBusy: false, workspaceSuspended: false })
  }
  reportEditingScenes(true)
}

export function useWorkspaceWindow(): WindowCloseStage | null {
  const [closeStage, setCloseStage] = useState<WindowCloseStage | null>(null)
  useEffect(() => {
    let workspaceToken: string | null = null
    let workspaceReason = ''
    let closing = false
    const lock = (stage?: WindowCloseStage): void => {
      setHotkeysEnabled(false)
      document.querySelector('.shell, .app-shell')?.setAttribute('inert', '')
      flushSync(() => {
        if (stage) setCloseStage(stage)
        useProjectStore.setState({ workspaceBusy: true })
      })
    }
    const resume = (): void => {
      if (workspaceToken || closing || useProjectStore.getState().workspaceRecovering) return
      flushSync(() => useProjectStore.setState({ workspaceBusy: false, workspaceSuspended: false }))
      const state = useProjectStore.getState()
      if (!state.changingSceneStructure && !state.closingProject) document.querySelector('.shell, .app-shell')?.removeAttribute('inert')
      reportEditingScenes(true)
    }
    const fail = (error: unknown): never => {
      useHostBridgeStore.getState().pushToast(i18n.t('toast.saveFailed').replace('{0}', error instanceof Error ? error.message : String(error)))
      throw error
    }
    const suspend = async (): Promise<void> => {
      await flushWindowWrites()
      // Cleanup writes still address the old workspace. Unmount while its
      // gate is yielded, then drain those writes before permitting mutation.
      flushSync(() => useProjectStore.setState({ workspaceSuspended: true }))
      await flushWindowWrites()
    }
    const stopWorkspace = window.novalist.onWorkspaceEvent?.(async (event) => {
      if (event.phase === 'prepare') {
        if (closing || workspaceToken) throw new Error(i18n.t('update.workspaceBusy'))
        if (hasWorkspaceDialogDraft()) throw new Error(i18n.t('update.workspaceBusy'))
        workspaceToken = event.preparation.token
        workspaceReason = event.preparation.reason
        useProjectStore.setState({ workspaceTransitionToken: workspaceToken })
        lock()
        try { await suspend() } catch (error) { fail(error) }
      } else if (event.phase === 'changed') {
        // Keep surfaces unmounted and input frozen through the apply ACK.
        // Data hydration happens only after main has advanced our wire epoch.
        flushSync(() => {
          useProjectStore.getState().applyWorkspaceSnapshot(event.snapshot,
            ['backup/restore', 'project/open', 'project/create', 'project/close', 'backup/restoreAsNewProject'].includes(workspaceReason))
          resetManuscriptWorkspace()
        })
      } else if (event.token === workspaceToken) {
        if (event.phase === 'resumed') {
          await useProjectStore.getState().refreshWorkspaceEditors()
          if (!useProjectStore.getState().isLoaded) await prepareProjectLibrary()
        }
        if (event.token !== workspaceToken) return
        workspaceToken = null
        useProjectStore.setState({ workspaceTransitionToken: null })
        resume()
        if (event.phase === 'resumed') {
          const state = useProjectStore.getState()
          if (state.isLoaded) {
            void state.loadRecents().catch(() => {})
            void useSettingsStore.getState().load().catch(() => {})
            void state.loadDrafts().catch(() => {})
            void useCodexStore.getState().refresh().catch(() => {})
          }
        }
      }
    })
    const stopClose = window.novalist.onBeforeClose?.(async (stage) => {
      if (stage === 'abort') {
        closing = false
        flushSync(() => setCloseStage(null))
        clearCloseBackupHandledForQuit()
        resume()
        return
      }
      try {
        if (workspaceToken || hasWorkspaceDialogDraft() || useProjectStore.getState().workspaceRecovering || useProjectStore.getState().changingSceneStructure || useProjectStore.getState().closingProject) {
          throw new Error(i18n.t('update.workspaceBusy'))
        }
        closing = true
        lock(stage)
        if (stage === 'flush') await suspend()
        else {
          await createCloseBackup()
          await flushWindowWrites()
          markCloseBackupHandledForQuit()
        }
      } catch (error) { fail(error) }
    })
    const stopRecovery = rpc.onRecovery((error) => {
      workspaceToken = null
      closing = false
      setCloseStage(null)
      lock()
      useProjectStore.setState({ workspaceRecovering: true, workspaceRecoveryError: error ?? null, workspaceTransitionToken: null })
      capturePendingWrites()
    })
    return () => { stopWorkspace?.(); stopClose?.(); stopRecovery() }
  }, [])
  return closeStage
}
