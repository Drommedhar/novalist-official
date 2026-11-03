import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useSettingsStore } from '../stores/settingsStore'
import { useProjectStore } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'
import { loadSpellDictionary } from '../proofing/spellDictionary'

/**
 * Keeps the platform spell checker in step with the writer's settings.
 *
 * The session that owns the checker lives in the main process, and the words the
 * writer taught it live in the settings file, so this is the one place that
 * knows about both. Mounted once from the shell.
 */
/**
 * Teaches the checker a word, everywhere it has to be taught.
 *
 * Three places, and missing any one of them is what "I added it and it is still
 * red" looks like: the writer's settings so it survives a restart and travels
 * to their other machines, the platform session so the underline under the word
 * they just right-clicked goes away now, and LanguageTool's own list so the
 * grammar side stops flagging it too. Local dictionary filtering also works
 * with the free LanguageTool endpoint and with Harper.
 *
 * Returns the writer's list as it stands after the word was learned.
 */
export async function learnWord(word: string): Promise<string[]> {
  const clean = word.trim()
  if (clean.length === 0) return []

  const words = await rpc.request<string[]>('spell/addWord', [clean])

  const effective = useSettingsStore.getState().view?.effective
  await Promise.all([
    window.novalist.applySpellCheck(
      effective?.spellCheckEnabled ?? true,
      effective?.spellCheckLanguages ?? [],
      words
    ),
    // Recheck every open editor immediately. Saving the list alone left the
    // grammar underline in place until the next edit.
    useSettingsStore.getState().load()
  ])

  // Best-effort: this one needs LanguageTool Plus credentials, and a writer
  // without them must still get the other two.
  void rpc.request<boolean>('grammar/addToDictionary', [clean]).catch(() => false)
  return words
}

function updatePlatformDictionary(enabled: boolean, languages: string, applied: () => void): () => void {
  let active = true
  let controller = new AbortController()
  let retry: ReturnType<typeof setTimeout> | undefined
  const update = async (): Promise<void> => {
    try {
      const words = await loadSpellDictionary(controller)
      if (!active || controller.signal.aborted) return
      await window.novalist.applySpellCheck(enabled, languages ? languages.split(',') : [], words)
      if (active && !controller.signal.aborted) applied()
    } catch {
      if (!active || !controller.signal.aborted) return
      retry = setTimeout(() => { controller = new AbortController(); void update() }, 1000)
    }
  }
  void update()
  return () => { active = false; clearTimeout(retry); controller.abort() }
}

export function useSpellCheck(): void {
  const { t } = useTranslation()
  const view = useSettingsStore((s) => s.view)
  const enabled = view?.effective.spellCheckEnabled ?? true
  const workspaceBusy = useProjectStore((state) => state.workspaceBusy)
  const workspaceEpoch = useProjectStore((state) => state.workspaceEpoch)
  const mobileSceneId = useProjectStore((state) => window.novalist.isMobile ? state.openSceneId : null)
  const writing = useShellStore((state) => !window.novalist.isMobile ||
    (state.mobileLayout === 'phone' ? state.mobileTab === 'manuscript' :
      state.mainView === 'write' || state.mainView === 'manuscript'))
  // Joined because the array identity changes on every settings fetch, which
  // would otherwise reload dictionaries on every unrelated settings write.
  const languages = (view?.effective.spellCheckLanguages ?? []).join(',')
  const dictionaryApplied = useRef(false)

  useEffect(() => {
    dictionaryApplied.current = false
  }, [enabled, languages, workspaceBusy, workspaceEpoch, writing])

  useEffect(() => {
    if (workspaceBusy || !writing || (window.novalist.isMobile && !mobileSceneId) || dictionaryApplied.current) return
    return updatePlatformDictionary(enabled, languages, () => { dictionaryApplied.current = true })
  }, [enabled, languages, workspaceBusy, workspaceEpoch, writing, mobileSceneId])

  // The spelling menu is built natively, so its labels have to be pushed across
  // rather than translated where it is shown.
  useEffect(() => {
    window.novalist.setSpellCheckMenuLabels({
      addToDictionary: t('spell.addToDictionary'),
      noSuggestions: t('spell.noSuggestions')
    })
  }, [t])

  // A word learned from the native menu is stored with the rest of the writer's
  // settings, so it survives a reinstall and travels to their other machines.
  useEffect(() => {
    window.novalist.onSpellCheckWordAdded((word) => {
      void learnWord(word)
    })
  }, [])
}
