import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'

/**
 * Spell-check settings: which languages the platform checker loads, and the
 * words the writer taught it.
 *
 * Languages are shown as the tags this Chromium build can actually load, so a
 * writer never picks one that silently does nothing. On macOS the list comes
 * back empty because the system checker decides for itself; the picker is
 * hidden there rather than showing an empty control.
 */
export function SpellCheckCard(props: {
  enabled: boolean
  languages: string[]
  onLanguagesChange: (languages: string[]) => void
}): React.JSX.Element | null {
  const { t } = useTranslation()
  const [available, setAvailable] = useState<string[]>([])
  const [words, setWords] = useState<string[]>([])

  useEffect(() => {
    void window.novalist.spellCheckLanguages().then(setAvailable)
    void rpc.request<string[]>('spell/words').then(setWords)
  }, [])

  if (!props.enabled) return null

  const toggleLanguage = (tag: string, checked: boolean): void => {
    props.onLanguagesChange(
      checked ? [...props.languages, tag] : props.languages.filter((l) => l !== tag)
    )
  }

  const forget = async (word: string): Promise<void> => {
    setWords(await rpc.request<string[]>('spell/removeWord', [word]))
  }

  return (
    <div className="settings-subgroup">
      {available.length > 0 && (
        <>
          <label className="inspector-label">{t('settings.spellCheckLanguages')}</label>
          <div className="settings-hint">{t('settings.spellCheckLanguagesHint')}</div>
          <div className="spell-language-grid">
            {available.map((tag) => (
              <label key={tag} className="relationships-toggle">
                <input
                  type="checkbox"
                  checked={props.languages.includes(tag)}
                  onChange={(e) => toggleLanguage(tag, e.target.checked)}
                />
                {tag}
              </label>
            ))}
          </div>
        </>
      )}

      <label className="inspector-label">{t('settings.spellCheckDictionary')}</label>
      <div className="settings-hint">
        {words.length === 0
          ? t('settings.spellCheckDictionaryEmpty')
          : t('settings.spellCheckDictionaryHint')}
      </div>
      {words.map((word) => (
        <div key={word} className="match-row">
          <span>{word}</span>
          <button
            className="dialog-button"
            title={t('settings.spellCheckForget')}
            onClick={() => void forget(word)}
          >
            <Trash2 size={14} />
          </button>
        </div>
      ))}
    </div>
  )
}
