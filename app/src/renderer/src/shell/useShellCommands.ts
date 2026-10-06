import { useEffect, useMemo } from 'react'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'
import { useSettingsStore } from '../stores/settingsStore'
import { buildDefaultHotkeys, installHotkeys, setHotkeysEnabled } from './hotkeys'
import { buildMenuLabels, buildMenuTemplate, OPEN_RECENT } from './menuLayout'
import { runCommand } from './commands'
import { runUpdateCheck } from './appUpdateActions'
import type { AppUpdateState } from './appUpdateState'

export function useShellCommands(updates: AppUpdateState): void {
  const { installing, updateOpen, installingRef, updateOpenRef, setUpdateProgress } = updates
  const isLoaded = useProjectStore((s) => s.isLoaded)
  const changingSceneStructure = useProjectStore((s) => s.changingSceneStructure)
  const workspaceBusy = useProjectStore((s) => s.workspaceBusy)
  const recentProjects = useProjectStore((s) => s.recentProjects)
  const mainView = useShellStore((s) => s.mainView)
  const hotkeys = useMemo(() => buildDefaultHotkeys(), [])

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
        if (data.command === 'help:checkUpdates') void runUpdateCheck(updates, true)
        else if (data.command.startsWith(OPEN_RECENT)) {
          void useProjectStore.getState().openProject(data.command.slice(OPEN_RECENT.length))
        } else runCommand(data.command)
      }
    }
    window.addEventListener('message', onMessage)
    return () => window.removeEventListener('message', onMessage)
  }, [])
}
