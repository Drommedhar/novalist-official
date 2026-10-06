import { useEffect, type RefObject } from 'react'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'
import { useUiScaleStore } from '../stores/uiScaleStore'
import { rpc } from '../rpc/client'
import { hydrateWindow } from './bootstrap'
import { hasSeenTour } from './FirstRunTour'
import { runUpdateCheck } from './appUpdateActions'
import type { AppUpdateState } from './appUpdateState'

export function useRecentProjects(): void {
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
}

export function useShellStartup(updates: AppUpdateState): void {
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
      .then(() => (window.novalist.autoUpdate ? runUpdateCheck(updates, false) : undefined))
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
}

export function useShellSize(shellRef: RefObject<HTMLDivElement | null>): void {
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
}

export function useDeepLinks(updates: AppUpdateState): void {
  const { installingRef } = updates
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
}

export function useProjectLanding(): void {
  const isLoaded = useProjectStore((s) => s.isLoaded)

  // Opening a project lands on the dashboard, matching the Avalonia app.
  useEffect(() => {
    if (isLoaded) useShellStore.getState().setMainView('dashboard')
  }, [isLoaded])

  // The tour is offered once, and only with a project open: every stop switches
  // to a real view, and a walk through eighteen empty ones teaches nothing.
  useEffect(() => {
    if (isLoaded && !hasSeenTour()) useShellStore.getState().setTourOpen(true)
  }, [isLoaded])

  // Mobile: the native Liquid Glass tab bar shows only inside a project.
  useEffect(() => {
    if (window.novalist.isMobile) window.novalist.setNavVisible?.(isLoaded)
  }, [isLoaded])
}
