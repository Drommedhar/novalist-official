import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Shuffle } from 'lucide-react'
import { rpc } from '../rpc/client'

interface Prompt {
  id: string
  kind: string
  text: string
}

interface Entry {
  key: string
  group: string
  name: string
  signals: string[]
}

interface ArticleSummary {
  id: string
  topic: string
  title: string
}

interface Article extends ArticleSummary {
  body: string
}

type Tab = 'prompt' | 'describe' | 'read'

/**
 * Reference a writer can reach without leaving the app.
 *
 * Novalist's lexicons were machine-readable analysis stems shown to nobody: it
 * could count filter words and could not help anybody find a better one. A
 * blank page had nothing behind it, and a scene needing a body-language beat
 * sent the writer to a browser, which is where writing sessions go to die.
 */
export function CraftPanel(): React.JSX.Element {
  const { t } = useTranslation()
  const [tab, setTab] = useState<Tab>('prompt')

  const [kinds, setKinds] = useState<string[]>([])
  const [kind, setKind] = useState('')
  // The index rather than the prompt: the backend owns the words, and keeping
  // the number here means "another" is one line and the same number always
  // gives the same prompt back.
  const [index, setIndex] = useState(() => Math.floor(Math.random() * 1000))
  const [prompt, setPrompt] = useState<Prompt | null>(null)

  const [query, setQuery] = useState('')
  const [entries, setEntries] = useState<Entry[]>([])

  const [articles, setArticles] = useState<ArticleSummary[]>([])
  const [reading, setReading] = useState<Article | null>(null)

  useEffect(() => {
    void rpc.request<string[]>('craft/promptKinds').then(setKinds).catch(() => setKinds([]))
    void rpc
      .request<ArticleSummary[]>('craft/articles')
      .then(setArticles)
      .catch(() => setArticles([]))
  }, [])

  const loadPrompt = useCallback(() => {
    void rpc
      .request<Prompt | null>('craft/prompt', [index, kind || null])
      .then(setPrompt)
      .catch(() => setPrompt(null))
  }, [index, kind])

  useEffect(loadPrompt, [loadPrompt])

  useEffect(() => {
    if (tab !== 'describe') return
    // Everything, then filtered here against the translated words. Asking the
    // backend to match would search the English behind the screen, which finds
    // nothing for anybody not reading in English.
    void rpc
      .request<Entry[]>('craft/lookup')
      .then(setEntries)
      .catch(() => setEntries([]))
  }, [tab])

  const text = query.trim().toLowerCase()
  const shown = entries.filter((entry) => {
    if (text.length === 0) return true
    const name = t(`craft.entry.${entry.key}.name`, { defaultValue: entry.name })
    const signals = entry.signals.map((s, i) =>
      t(`craft.entry.${entry.key}.signals.${i}`, { defaultValue: s })
    )
    return (
      name.toLowerCase().includes(text) ||
      signals.some((s) => s.toLowerCase().includes(text))
    )
  })

  return (
    <div className="craft">
      <div className="craft-tabs">
        {(['prompt', 'describe', 'read'] as Tab[]).map((key) => (
          <button
            key={key}
            className={`dialog-button${tab === key ? ' primary' : ''}`}
            onClick={() => setTab(key)}
          >
            {t(`craft.tab.${key}`)}
          </button>
        ))}
      </div>

      {tab === 'prompt' && (
        <>
          <select
            className="dialog-input"
            aria-label={t('craft.kind')}
            value={kind}
            onChange={(e) => setKind(e.target.value)}
          >
            <option value="">{t('craft.anyKind')}</option>
            {kinds.map((k) => (
              <option key={k} value={k}>
                {t(`craft.kindName.${k}`)}
              </option>
            ))}
          </select>
          <p className="craft-prompt">
            {prompt
              ? t(`craft.prompt.${prompt.id}`, { defaultValue: prompt.text })
              : t('craft.noPrompt')}
          </p>
          <button className="dialog-button" onClick={() => setIndex(index + 1)}>
            <Shuffle size={12} strokeWidth={2} />
            {t('craft.another')}
          </button>
        </>
      )}

      {tab === 'describe' && (
        <>
          <input
            className="dialog-input"
            placeholder={t('craft.lookupPlaceholder')}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
          {shown.length === 0 ? (
            <p className="inspector-meta">{t('craft.noMatch')}</p>
          ) : (
            shown.map((entry) => (
              <div key={entry.key} className="craft-entry">
                <span className="inspector-label">
                  {t(`craft.entry.${entry.key}.name`, { defaultValue: entry.name })}
                </span>
                <ul className="craft-signals">
                  {entry.signals.map((signal, i) => (
                    <li key={signal}>
                      {t(`craft.entry.${entry.key}.signals.${i}`, { defaultValue: signal })}
                    </li>
                  ))}
                </ul>
              </div>
            ))
          )}
        </>
      )}

      {tab === 'read' &&
        (reading ? (
          <>
            <button className="dialog-button" onClick={() => setReading(null)}>
              {t('craft.backToList')}
            </button>
            <span className="inspector-label">{reading.title}</span>
            {/* Paragraphs rather than one block: this is read while a scene is
                open, and a wall of text in a side panel is not read at all. */}
            {reading.body
              .trim()
              .split(/\n\s*\n/)
              .map((paragraph, i) => (
                <p key={i} className="craft-body">
                  {paragraph.trim().replace(/\s+/g, ' ')}
                </p>
              ))}
          </>
        ) : (
          <>
            <p className="inspector-meta">{t('craft.articlesEnglishOnly')}</p>
            {articles.map((article) => (
            <button
              key={article.id}
              className="craft-article-link"
              onClick={() =>
                void rpc
                  .request<Article | null>('craft/article', [article.id])
                  .then(setReading)
                  .catch(() => setReading(null))
              }
            >
              <span className="craft-article-topic">{article.topic}</span>
              <span>{article.title}</span>
            </button>
            ))}
          </>
        ))}
    </div>
  )
}
