import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { InputDialog } from '../../shell/InputDialog'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import './languages.css'

interface Word {
  id: string
  word: string
  meaning: string
  partOfSpeech: string
  pronunciation: string
  notes: string
}

interface Hit {
  languageId: string
  languageName: string
  word: Word
}

interface Language {
  id: string
  name: string
  description: string
  words: Word[]
}

const BLANK: Word = {
  id: '',
  word: '',
  meaning: '',
  partOfSpeech: '',
  pronunciation: '',
  notes: ''
}

/**
 * The invented languages of a project, and their dictionaries.
 *
 * Building one meant hand-rolling a custom entity type, which gets a writer a
 * list of entries and none of what a lexicon is actually for: looking a word up
 * mid-sentence, and finding out whether they have already coined it.
 *
 * The search reads both directions - the word and the meaning - because a
 * writer either has the invented word and wants the meaning, or has the meaning
 * and wants to know whether there is already a word.
 */
export function LanguagesView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [languages, setLanguages] = useState<Language[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  // Arriving from a coined word the writer hovered in the manuscript: land on
  // that word rather than on the whole dictionary.
  const pendingQuery = useShellStore((s) => s.pendingLanguageQuery)
  useEffect(() => {
    if (!pendingQuery) return
    setQuery(pendingQuery)
    useShellStore.getState().clearPendingLanguage()
  }, [pendingQuery])
  const [draft, setDraft] = useState<Word>(BLANK)
  const [adding, setAdding] = useState(false)
  // The same query across every other language. A writer coining a word wants
  // to know whether they already made one somewhere else, which is the one
  // question the list in front of them cannot answer.
  const [elsewhere, setElsewhere] = useState<Hit[]>([])
  const [removing, setRemoving] = useState(false)

  useEffect(() => {
    if (mainView !== 'languages') return
    void rpc
      .request<Language[]>('conlang/list')
      .then(setLanguages)
      .catch(() => setLanguages([]))
  }, [mainView])

  const selected = languages.find((l) => l.id === selectedId) ?? languages[0] ?? null

  useEffect(() => {
    const text = query.trim()
    if (text.length < 2 || languages.length < 2) {
      setElsewhere([])
      return
    }
    void rpc
      .request<Hit[]>('conlang/lookup', [text])
      .then((hits) => setElsewhere(hits.filter((h) => h.languageId !== selected?.id)))
      .catch(() => setElsewhere([]))
  }, [query, languages.length, selected?.id])

  const apply = (next: Language[]): void => {
    setLanguages(next)
    setDraft(BLANK)
  }

  const saveWord = async (): Promise<void> => {
    if (!selected || draft.word.trim().length === 0) return
    apply(
      await rpc.request<Language[]>('conlang/saveWord', [
        selected.id,
        draft.id || null,
        draft.word,
        draft.meaning,
        draft.partOfSpeech,
        draft.pronunciation,
        draft.notes
      ])
    )
  }

  // Filtered here rather than by a round trip: the dictionary is already on
  // screen, and a lookup that waits on the backend for every keystroke feels
  // slower than reading the list.
  const shown = (selected?.words ?? []).filter((w) => {
    const text = query.trim().toLowerCase()
    if (text.length === 0) return true
    return (
      w.word.toLowerCase().includes(text) ||
      w.meaning.toLowerCase().includes(text)
    )
  })

  return (
    <div className="dashboard languages">
      <h1 className="dashboard-title">{t('languages.title')}</h1>
      <p className="settings-hint">{t('languages.intro')}</p>

      <div className="languages-toolbar">
        <select
          className="dialog-input"
          aria-label={t('languages.language')}
          value={selected?.id ?? ''}
          onChange={(e) => setSelectedId(e.target.value)}
        >
          {languages.map((language) => (
            <option key={language.id} value={language.id}>
              {language.name}
            </option>
          ))}
          {languages.length === 0 && <option value="">{t('languages.none')}</option>}
        </select>
        <button className="toolbar-button toolbar-action" onClick={() => setAdding(true)}>
          <Plus size={14} strokeWidth={2} />
          {t('languages.add')}
        </button>
        {selected && (
          <button className="dialog-button" onClick={() => setRemoving(true)}>
            {t('explorer.contextDelete')}
          </button>
        )}
      </div>

      {!selected ? (
        <p className="codex-empty">{t('languages.emptyHint')}</p>
      ) : (
        <>
          <input
            className="dialog-input languages-description"
            placeholder={t('languages.describe')}
            defaultValue={selected.description}
            key={`${selected.id}-description`}
            onBlur={(e) => {
              if (e.target.value === selected.description) return
              void rpc
                .request<Language[]>('conlang/update', [selected.id, null, e.target.value])
                .then(setLanguages)
            }}
          />

          <div className="dashboard-card">
            <div className="git-section-header">
              <span className="git-section-title">
                {t('languages.words', { count: selected.words.length })}
              </span>
            </div>

            {/* Both directions: the word and the meaning. A search that only
                read the word would be a glossary, not a dictionary. */}
            <input
              className="dialog-input"
              placeholder={t('languages.searchPlaceholder')}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />

            {shown.length === 0 ? (
              <p className="codex-empty">
                {t(query.trim() ? 'languages.noMatch' : 'languages.noWords')}
              </p>
            ) : (
              <table className="plotgrid-table languages-table">
                <thead>
                  <tr>
                    <th>{t('languages.word')}</th>
                    <th>{t('languages.meaning')}</th>
                    <th>{t('languages.partOfSpeech')}</th>
                    <th>{t('languages.pronunciation')}</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {shown.map((word) => (
                    <tr key={word.id}>
                      <td>{word.word}</td>
                      <td>{word.meaning}</td>
                      <td>{word.partOfSpeech}</td>
                      <td>{word.pronunciation}</td>
                      <td className="languages-row-actions">
                        <button className="dialog-button" onClick={() => setDraft(word)}>
                          {t('dialog.edit')}
                        </button>
                        <button
                          className="dialog-button"
                          onClick={() =>
                            void rpc
                              .request<Language[]>('conlang/deleteWord', [selected.id, word.id])
                              .then(apply)
                          }
                        >
                          {t('explorer.contextDelete')}
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}

            {elsewhere.length > 0 && (
              <div className="languages-elsewhere">
                <span className="inspector-label">{t('languages.elsewhere')}</span>
                {elsewhere.map((hit) => (
                  <div key={`${hit.languageId}-${hit.word.id}`} className="languages-elsewhere-row">
                    <button
                      className="style-continuity-jump"
                      onClick={() => setSelectedId(hit.languageId)}
                    >
                      {hit.languageName}
                    </button>
                    <span>
                      {hit.word.word} - {hit.word.meaning}
                    </span>
                  </div>
                ))}
              </div>
            )}

            {/* One row of fields rather than a dialog: coining words happens in
                runs, and a dialog per word turns ten into thirty clicks. */}
            <div className="languages-draft">
              <input
                className="dialog-input"
                placeholder={t('languages.word')}
                value={draft.word}
                onChange={(e) => setDraft({ ...draft, word: e.target.value })}
              />
              <input
                className="dialog-input"
                placeholder={t('languages.meaning')}
                value={draft.meaning}
                onChange={(e) => setDraft({ ...draft, meaning: e.target.value })}
              />
              <input
                className="dialog-input"
                placeholder={t('languages.partOfSpeech')}
                value={draft.partOfSpeech}
                onChange={(e) => setDraft({ ...draft, partOfSpeech: e.target.value })}
              />
              <input
                className="dialog-input"
                placeholder={t('languages.pronunciation')}
                value={draft.pronunciation}
                onChange={(e) => setDraft({ ...draft, pronunciation: e.target.value })}
              />
              <button
                className="dialog-button primary"
                disabled={draft.word.trim().length === 0}
                onClick={() => void saveWord()}
              >
                {draft.id ? t('dialog.save') : t('languages.addWord')}
              </button>
            </div>
          </div>
        </>
      )}

      {adding && (
        <InputDialog
          title={t('languages.add')}
          onCancel={() => setAdding(false)}
          onSubmit={(name) => {
            setAdding(false)
            void rpc.request<Language[]>('conlang/create', [name]).then((next) => {
              setLanguages(next)
              setSelectedId(next[next.length - 1]?.id ?? null)
            })
          }}
        />
      )}
      {removing && selected && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={t('languages.deleteHint', { name: selected.name })}
          onCancel={() => setRemoving(false)}
          onConfirm={() => {
            setRemoving(false)
            void rpc.request<Language[]>('conlang/delete', [selected.id]).then((next) => {
              setLanguages(next)
              setSelectedId(next[0]?.id ?? null)
            })
          }}
        />
      )}
    </div>
  )
}
