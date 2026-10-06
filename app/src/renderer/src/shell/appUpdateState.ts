import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { StoreUpdate } from '../stores/extensionsStore'

export function useAppUpdateState() {
  const { t } = useTranslation()
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

  return {
    appUpdate, setAppUpdate, extUpdates, setExtUpdates, updatingExtId, setUpdatingExtId, updateOpen, setUpdateOpen, updateOpenRef, updateProgress, setUpdateProgress, downloading, setDownloading, installingRef, transferRef, launchTokenRef, checkingRef, installing, setInstalling, ready, setReady, manualInstall, setManualInstall, checking, setChecking, updateNotice, setUpdateNotice, appVersion, setAppVersion, updateError, setUpdateError, updateErrorMessage, t
  }
}

export type AppUpdateState = ReturnType<typeof useAppUpdateState>
