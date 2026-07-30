import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Plus, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'

interface KeywordDto {
  id: string
  name: string
  color: string
  parentId: string
  sceneCount: number
}

/**
 * The book's keyword vocabulary.
 *
 * Scene tags were free text with nothing behind them: no registry, no colours,
 * no rename. So "flashback", "Flashback" and "flash-back" were three tags, and
 * correcting that meant opening every scene that used the wrong one.
 *
 * Renaming here is a real rename — the registry entry and every scene tagged
 * with it change together, which is the whole reason a registry is worth having.
 */
export function KeywordsCard(): React.JSX.Element {
  const { t } = useTranslation()
  const [keywords, setKeywords] = useState<KeywordDto[]>([])
  const [dirty, setDirty] = useState(false)

  useEffect(() => {
    void rpc.request<KeywordDto[]>('keywords/list').then(setKeywords).catch(() => setKeywords([]))
  }, [])

  const edit = (index: number, patch: Partial<KeywordDto>): void => {
    setDirty(true)
    setKeywords(keywords.map((k, i) => (i === index ? { ...k, ...patch } : k)))
  }

  const save = (): void => {
    void rpc.request<KeywordDto[]>('keywords/save', [keywords]).then((saved) => {
      setKeywords(saved)
      setDirty(false)
    })
  }

  return (
    <div className="settings-subgroup">
      <div className="settings-hint">{t('keywords.intro')}</div>

      {keywords.map((keyword, index) => (
        <div key={keyword.id} className="match-row">
          <input
            className="inspector-input"
            value={keyword.name}
            placeholder={t('keywords.namePlaceholder')}
            onChange={(e) => edit(index, { name: e.target.value })}
            onBlur={() => {
              // A rename has to reach the scenes, so it is its own operation
              // rather than part of the bulk save.
              const original = keyword.id
              if (!original) return
              void rpc
                .request<KeywordDto[]>('keywords/rename', [original, keyword.name])
                .then(setKeywords)
            }}
          />
          <input
            className="dialog-input settings-color"
            type="color"
            aria-label={t('keywords.colour')}
            value={keyword.color}
            onChange={(e) => edit(index, { color: e.target.value })}
          />
          <select
            className="inspector-input"
            aria-label={t('keywords.parent')}
            value={keyword.parentId}
            onChange={(e) => edit(index, { parentId: e.target.value })}
          >
            <option value="">{t('keywords.noParent')}</option>
            {keywords
              .filter((other) => other.id !== keyword.id && !other.parentId)
              .map((other) => (
                <option key={other.id} value={other.id}>
                  {other.name}
                </option>
              ))}
          </select>
          <span className="settings-hint">
            {t('keywords.usedIn', { count: keyword.sceneCount })}
          </span>
          <button
            className="binder-row-action"
            aria-label={t('keywords.delete')}
            title={t('keywords.delete')}
            onClick={() =>
              void rpc.request<KeywordDto[]>('keywords/delete', [keyword.id, true]).then(setKeywords)
            }
          >
            <Trash2 size={15} strokeWidth={2} />
          </button>
        </div>
      ))}

      <div className="match-row">
        <button
          className="btn-secondary"
          onClick={() => {
            setDirty(true)
            setKeywords([
              ...keywords,
              { id: '', name: '', color: '#8b8b8b', parentId: '', sceneCount: 0 }
            ])
          }}
        >
          <Plus size={15} strokeWidth={2} />
          {t('keywords.add')}
        </button>
        {/* Without this a project with two hundred tags starts on an empty
            registry, which makes the feature useless to whoever needs it most. */}
        <button
          className="btn-secondary"
          onClick={() => void rpc.request<KeywordDto[]>('keywords/harvest').then(setKeywords)}
        >
          {t('keywords.harvest')}
        </button>
        <button className="btn-primary" disabled={!dirty} onClick={save}>
          {t('keywords.save')}
        </button>
      </div>
    </div>
  )
}
