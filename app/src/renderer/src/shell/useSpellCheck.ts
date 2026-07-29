import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../rpc/client'
import { useSettingsStore } from '../stores/settingsStore'

/**
 * Keeps the platform spell checker in step with the writer's settings.
 *
 * The session that owns the checker lives in the main process, and the words the
 * writer taught it live in the settings file, so this is the one place that
 * knows about both. Mounted once from the shell.
 */
export function useSpellCheck(): void {
  const { t } = useTranslation()
  const view = useSettingsStore((s) => s.view)
  const enabled = view?.effective.spellCheckEnabled ?? true
  // Joined because the array identity changes on every settings fetch, which
  // would otherwise reload dictionaries on every unrelated settings write.
  const languages = (view?.effective.spellCheckLanguages ?? []).join(',')

  useEffect(() => {
    void (async () => {
      // Every name the Codex holds as well as the writer's own words: a
      // secondary-world manuscript is a wall of underlines otherwise.
      const words = await rpc.request<string[]>('spell/dictionary')
      await window.novalist.applySpellCheck(
        enabled,
        languages.length > 0 ? languages.split(',') : [],
        words
      )
    })()
  }, [enabled, languages])

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
      void rpc.request('spell/addWord', [word])
    })
  }, [])
}
