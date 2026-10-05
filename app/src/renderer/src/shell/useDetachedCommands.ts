import { useEffect } from 'react'
import { commandById, runCommand } from './commands'
import { buildDefaultHotkeys, installHotkeys, setHotkeysEnabled } from './hotkeys'
import { useProjectStore } from '../stores/projectStore'

const LOCAL_WINDOW_COMMANDS = new Set([
  'app.print', 'app.splitRight', 'app.splitDown', 'app.closePane', 'app.resetPanes',
  'app.popOut', 'app.paneLayouts', 'app.uiScaleIncrease', 'app.uiScaleDecrease', 'app.uiScaleReset', 'app.about'
])

/** Editing commands belong to the focused pane. Application/project dialogs
 * belong to the main shell, which owns their navigation and global UI. */
export function useDetachedCommands(): void {
  const busy = useProjectStore((state) => state.workspaceBusy || state.changingSceneStructure || state.closingProject)
  useEffect(() => {
    setHotkeysEnabled(!busy)
    if (busy) return
    const run = (id: string): void => {
      if (useProjectStore.getState().workspaceBusy) return
      const command = commandById(id)
      if (command && (['selection', 'caret', 'paragraph', 'view'].includes(command.scope) || LOCAL_WINDOW_COMMANDS.has(id) || id.startsWith('view.'))) runCommand(id)
      else window.novalist.forwardMainCommand?.(id)
    }
    const uninstall = installHotkeys(buildDefaultHotkeys().map((action) => ({ ...action, run: () => run(action.actionId) })))
    const receive = (event: MessageEvent): void => {
      if (event.source !== window || event.data?.novalist !== 'menu-command' || typeof event.data.command !== 'string') return
      run(event.data.command)
    }
    window.addEventListener('message', receive)
    return () => { uninstall(); window.removeEventListener('message', receive) }
  }, [busy])
}
