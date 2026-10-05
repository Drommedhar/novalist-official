import { rpc } from '../rpc/client'
import { useShellStore } from '../stores/shellStore'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { useExtensionsStore } from '../stores/extensionsStore'
import { useSettingsStore } from '../stores/settingsStore'
import { loadUserAssets, watchUserAssets } from '../stores/userAssets'
import type { PingResult } from '../rpc/contract'
import { loadWorkspaceSnapshot } from './useWorkspaceWindow'

export async function hydrateWindow(): Promise<void> {
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
  if (window.novalist.workspaceSnapshot) await loadWorkspaceSnapshot()
  else {
    const state = await rpc.request<ProjectStateDto>('project/getState')
    useProjectStore.getState().applyState(state)
  }
  await useProjectStore.getState().loadRecents()
  await useExtensionsStore.getState().load()
}
