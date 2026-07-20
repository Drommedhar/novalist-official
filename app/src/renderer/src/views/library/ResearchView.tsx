import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ExternalLink, FolderOpen, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { useShellStore } from '../../stores/shellStore'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import './library.css'

interface ResearchItemDto {
  id: string
  title: string
  type: string
  content: string
  tags: string[]
  fileSize: string
  modified: string
}

const TYPES = ['Note', 'Link', 'File', 'Image', 'Pdf']

const isFileType = (type: string): boolean =>
  type === 'File' || type === 'Image' || type === 'Pdf'

export function ResearchView(): React.JSX.Element {
  const { t } = useTranslation()
  const mainView = useShellStore((s) => s.mainView)
  const [items, setItems] = useState<ResearchItemDto[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [newTag, setNewTag] = useState('')
  const [confirmDelete, setConfirmDelete] = useState(false)

  useEffect(() => {
    if (mainView !== 'research') return
    void rpc.request<ResearchItemDto[]>('research/list').then(setItems)
  }, [mainView])

  const selected = items.find((i) => i.id === selectedId) ?? null

  const query = search.trim().toLowerCase()
  const filtered =
    query.length === 0
      ? items
      : items.filter(
          (i) =>
            i.title.toLowerCase().includes(query) ||
            i.content.toLowerCase().includes(query) ||
            i.tags.some((tag) => tag.toLowerCase().includes(query))
        )

  const save = async (item: ResearchItemDto): Promise<void> => {
    const updated = await rpc.request<ResearchItemDto[]>('research/save', [
      item.id,
      item.title,
      item.type,
      item.content,
      item.tags
    ])
    setItems(updated)
  }

  const patchSelected = (patch: Partial<ResearchItemDto>): void => {
    if (!selected) return
    setItems(items.map((i) => (i.id === selected.id ? { ...i, ...patch } : i)))
  }

  const create = (type: string, content: string): void => {
    void rpc
      .request<ResearchItemDto[]>('research/save', [null, t('research.titleWatermark'), type, content, []])
      .then((updated) => {
        setItems(updated)
        setSelectedId(updated[updated.length - 1]?.id ?? null)
      })
  }

  const importFile = async (): Promise<void> => {
    const path = await window.novalist.pickFile(t('research.importFile'), 'all')
    if (!path) return
    const updated = await rpc.request<ResearchItemDto[]>('research/import', [path])
    setItems(updated)
    setSelectedId(updated[updated.length - 1]?.id ?? null)
  }

  const addTag = (): void => {
    if (!selected) return
    const tag = newTag.trim()
    if (tag.length === 0) return
    if (selected.tags.some((existing) => existing.toLowerCase() === tag.toLowerCase())) {
      setNewTag('')
      return
    }
    const next = { ...selected, tags: [...selected.tags, tag] }
    patchSelected({ tags: next.tags })
    void save(next)
    setNewTag('')
  }

  const removeTag = (tag: string): void => {
    if (!selected) return
    const next = { ...selected, tags: selected.tags.filter((existing) => existing !== tag) }
    patchSelected({ tags: next.tags })
    void save(next)
  }

  return (
    <div className="codex">
      <div className="codex-body">
        <div className="codex-list">
          <div className="research-actions">
            <button className="research-action-btn" onClick={() => create('Note', '')}>
              {t('research.addNote')}
            </button>
            <button className="research-action-btn" onClick={() => create('Link', 'https://')}>
              {t('research.addLink')}
            </button>
            <button className="research-action-btn" onClick={() => void importFile()}>
              {t('research.importFile')}
            </button>
          </div>
          <input
            className="dialog-input research-search"
            placeholder={t('research.search')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          <div className="codex-nav-scroll">
            {filtered.map((item) => (
              <button
                key={item.id}
                className={`codex-row${selectedId === item.id ? ' active' : ''}`}
                onClick={() => setSelectedId(item.id)}
              >
                <span className="codex-row-text">
                  <span className="codex-row-name">{item.title}</span>
                  <span className="codex-row-detail">
                    {t(`research.type${item.type}`, { defaultValue: item.type })}
                  </span>
                </span>
              </button>
            ))}
            {filtered.length === 0 && <p className="codex-empty">{t('research.empty')}</p>}
          </div>
        </div>
        <div className="codex-detail">
          {selected ? (
            <div className="research-editor">
              <div className="codex-detail-actions">
                {(selected.type === 'Link' || isFileType(selected.type)) && (
                  <button
                    className="dialog-button"
                    onClick={() => void window.novalist.openExternal(selected.content)}
                  >
                    <ExternalLink size={13} strokeWidth={2} /> {t('research.openExternal')}
                  </button>
                )}
                {isFileType(selected.type) && (
                  <button
                    className="dialog-button"
                    onClick={() => void window.novalist.revealPath(selected.content)}
                  >
                    <FolderOpen size={13} strokeWidth={2} /> {t('research.reveal')}
                  </button>
                )}
                <button className="dialog-button danger" onClick={() => setConfirmDelete(true)}>
                  <Trash2 size={13} strokeWidth={2} /> {t('research.deleteItem')}
                </button>
              </div>
              <input
                className="dialog-input"
                placeholder={t('research.titleWatermark')}
                value={selected.title}
                onChange={(e) => patchSelected({ title: e.target.value })}
                onBlur={() => void save(selected)}
              />
              <select
                className="dialog-input"
                value={selected.type}
                onChange={(e) => {
                  patchSelected({ type: e.target.value })
                  void save({ ...selected, type: e.target.value })
                }}
              >
                {TYPES.map((type) => (
                  <option key={type} value={type}>
                    {t(`research.type${type}`, { defaultValue: type })}
                  </option>
                ))}
              </select>
              {selected.type === 'Image' && selected.content.length > 0 && (
                <div className="research-preview">
                  <img
                    src={`novalist-project://nl/${encodeURI(selected.content)}`}
                    alt={selected.title}
                  />
                </div>
              )}
              {isFileType(selected.type) && selected.content.length > 0 && (
                <dl className="research-meta">
                  <dt>{t('research.metadata')}</dt>
                  <dd className="research-meta-path">{selected.content}</dd>
                  {(selected.fileSize.length > 0 || selected.modified.length > 0) && (
                    <dd className="research-meta-stats">
                      {[selected.fileSize, selected.modified].filter((s) => s.length > 0).join(' · ')}
                    </dd>
                  )}
                </dl>
              )}
              <textarea
                className="inspector-textarea research-content"
                rows={12}
                placeholder={t('research.contentWatermark')}
                value={selected.content}
                onChange={(e) => patchSelected({ content: e.target.value })}
                onBlur={() => void save(selected)}
              />
              <div className="research-tags">
                <span className="research-tags-label">{t('research.tags')}</span>
                <div className="research-tag-list">
                  {selected.tags.map((tag) => (
                    <span key={tag} className="research-tag">
                      {tag}
                      <button
                        className="research-tag-remove"
                        aria-label={`${t('explorer.contextDelete')} ${tag}`}
                        onClick={() => removeTag(tag)}
                      >
                        ×
                      </button>
                    </span>
                  ))}
                </div>
                <div className="research-tag-add">
                  <input
                    className="dialog-input"
                    placeholder={t('research.addTag')}
                    value={newTag}
                    onChange={(e) => setNewTag(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') addTag()
                    }}
                  />
                  <button className="dialog-button" onClick={addTag}>
                    +
                  </button>
                </div>
              </div>
            </div>
          ) : (
            <p className="codex-empty">{t('research.empty')}</p>
          )}
        </div>
      </div>
      {confirmDelete && selected && (
        <ConfirmDialog
          title={t('research.confirmDeleteTitle')}
          message={selected.title}
          onCancel={() => setConfirmDelete(false)}
          onConfirm={() => {
            setConfirmDelete(false)
            void rpc.request<ResearchItemDto[]>('research/delete', [selected.id]).then((updated) => {
              setItems(updated)
              setSelectedId(null)
            })
          }}
        />
      )}
    </div>
  )
}
