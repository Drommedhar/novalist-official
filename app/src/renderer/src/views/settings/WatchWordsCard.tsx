import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { rpc } from '../../rpc/client'

/**
 * Words the writer asked the style report to count.
 *
 * Novalist had no local flagged-word list at all - no way to catch every
 * "suddenly", or to hold one spelling of a series-bible term. The report
 * already counts adverbs and filter words against a bundled lexicon; this is
 * the same machinery pointed at a list the writer owns.
 */
export function WatchWordsCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [text, setText] = useState('')

  useEffect(() => {
    void rpc
      .request<string[]>('style/watchWords')
      .then((words) => setText(words.join('\n')))
      .catch(() => setText(''))
  }, [])

  return (
    <div className="settings-subgroup">
      <label className="inspector-label" htmlFor="set-watch-words">
        {t('style.watchWords')}
      </label>
      <textarea
        id="set-watch-words"
        className="dialog-input premise-paragraph"
        placeholder={t('style.watchWordsPlaceholder')}
        value={text}
        onChange={(e) => setText(e.target.value)}
        onBlur={() =>
          void rpc
            .request<string[]>('style/setWatchWords', [text.split('\n')])
            .then((words) => setText(words.join('\n')))
        }
      />
      <div className="settings-hint">{t('style.watchWordsDesc')}</div>
    </div>
  )
}
