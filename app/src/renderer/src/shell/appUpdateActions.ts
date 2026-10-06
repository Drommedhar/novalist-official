import type { AppUpdateState } from './appUpdateState'
import { useExtensionsStore, type StoreUpdate } from '../stores/extensionsStore'
import { flushWindowWrites } from './useWorkspaceWindow'
import {
  clearCloseBackupHandledForQuit, createCloseBackup, markCloseBackupHandledForQuit
} from './useBackupScheduler'

export async function runUpdateCheck(state: AppUpdateState, manual: boolean): Promise<void> {
  const { setAppUpdate, setExtUpdates, setUpdateOpen, installingRef, transferRef, launchTokenRef, checkingRef, setManualInstall, setChecking, setUpdateNotice, setUpdateError } = state
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

export async function updateExtension(state: AppUpdateState, u: StoreUpdate): Promise<void> {
  const { setExtUpdates, setUpdatingExtId, setUpdateError, t } = state
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

export async function downloadAppUpdate(state: AppUpdateState): Promise<void> {
  const { appUpdate, setUpdateProgress, setDownloading, transferRef, launchTokenRef, setReady, setManualInstall, setUpdateNotice, setUpdateError } = state
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

export async function installAppUpdate(state: AppUpdateState): Promise<void> {
  const { installingRef, launchTokenRef, setInstalling, setReady, setUpdateError } = state
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
