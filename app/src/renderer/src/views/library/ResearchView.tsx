import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { RevisionsPanel } from '../../shell/RevisionsPanel'
import { MarkdownEditor } from '../../shell/MarkdownEditor'
import { ExternalLink, FolderOpen, Inbox, Link2, Star, Trash2 } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { ImportVaultDialog } from '../../shell/ImportVaultDialog'
import { ScratchpadPanel } from '../../shell/ScratchpadPanel'
import { useShellStore } from '../../stores/shellStore'
import { ConfirmDialog } from '../../shell/ConfirmDialog'
import { CustomFieldsPanel } from '../../shell/CustomFieldsPanel'
import { EntityTypeDialog } from '../../shell/EntityTypeDialog'
import { AppendToEntityDialog } from '../../shell/AppendToEntityDialog'
import './library.css'

/** Reserved tag marking a quick-captured note that has not been filed yet.
 *  Mirrors ResearchItem.InboxTag on the backend. */
const INBOX_TAG = 'inbox'

interface ResearchItemDto {
  id: string
  title: string
  type: string
  content: string
  tags: string[]
  fileSize: string
  modified: string
  entityRefs: string[]
  /** "None", "Open", "InProgress" or "Resolved". */
  status: string
  /** 0 for unrated, 1-5 otherwise. */
  rating: number
  /** Ids of other research items this one refers to. */
  relatedIds: string[]
}

const TYPES = ['Note', 'Link', 'File', 'Image', 'Pdf', 'Audio', 'Video']

/** Where an item stands. Short on purpose - this is not a task tracker. */
const STATUSES = ['None', 'Open', 'InProgress', 'Resolved']

const isFileType = (type: string): boolean =>
  type === 'File' ||
  type === 'Image' ||
  type === 'Pdf' ||
  type === 'Audio' ||
  type === 'Video'

export function ResearchView(): React.JSX.Element {
  const { t } = useTranslation()
  const pendingResearchId = useShellStore((s) => s.pendingResearchId)
  const clearPendingResearch = useShellStore((s) => s.clearPendingResearch)
  const [items, setItems] = useState<ResearchItemDto[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [vaultOpen, setVaultOpen] = useState(false)
  const [newTag, setNewTag] = useState('')
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [inboxOnly, setInboxOnly] = useState(false)
  const [dragging, setDragging] = useState(false)
  const [fetchingTitle, setFetchingTitle] = useState(false)
  // Every Codex entry, so a research item can be linked to what it is about.
  const [allEntities, setAllEntities] = useState<{ id: string; name: string }[]>([])
  // Filing an inbox note into the Codex: which dialog is open, if any.
  const [filing, setFiling] = useState<'create' | 'append' | null>(null)

  useEffect(() => {
    void rpc.request<ResearchItemDto[]>('research/list').then(setItems)
  }, [])

  // Quick-open (and other deep links) can ask for a specific item; select it once
  // the list has loaded, then clear the request so it fires only once.
  useEffect(() => {
    if (!pendingResearchId) return
    if (!items.some((i) => i.id === pendingResearchId)) return
    setSelectedId(pendingResearchId)
    setSearch('')
    clearPendingResearch()
  }, [pendingResearchId, items, clearPendingResearch])

  const selected = items.find((i) => i.id === selectedId) ?? null

  // Load the Codex once per visit so links can be picked and shown by name.
  useEffect(() => {
    let cancelled = false
    const load = async (): Promise<void> => {
      const types = ['character', 'location', 'item', 'lore']
      try {
        const custom = await rpc.request<{ typeKey: string }[]>('entities/customTypes')
        types.push(...custom.map((c) => c.typeKey))
      } catch {
        // Built-ins alone still make the picker useful.
      }
      const all: { id: string; name: string }[] = []
      for (const typeKey of types) {
        try {
          const list = await rpc.request<{ id: string; name: string }[]>('entities/list', [typeKey])
          all.push(...list.map((e) => ({ id: e.id, name: e.name })))
        } catch {
          // Skip a type that fails rather than losing the whole picker.
        }
      }
      all.sort((a, b) => a.name.localeCompare(b.name))
      if (!cancelled) setAllEntities(all)
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [])

  const entityNames = new Map(allEntities.map((e) => [e.id, e.name]))

  const isInbox = (item: ResearchItemDto): boolean =>
    item.tags.some((tag) => tag.toLowerCase() === INBOX_TAG)
  const inboxCount = items.filter(isInbox).length

  const query = search.trim().toLowerCase()
  const filtered = items
    .filter((i) => !inboxOnly || isInbox(i))
    .filter(
      (i) =>
        query.length === 0 ||
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
      item.tags,
      item.entityRefs
    ])
    setItems(updated)
  }

  const patchSelected = (patch: Partial<ResearchItemDto>): void => {
    if (!selected) return
    setItems(items.map((i) => (i.id === selected.id ? { ...i, ...patch } : i)))
  }

  const create = (type: string, content: string): void => {
    void rpc
      .request<ResearchItemDto[]>('research/save', [
        null,
        t('research.titleWatermark'),
        type,
        content,
        [],
        []
      ])
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

  /** Files dropped onto the list are imported exactly like picked ones; dropped
   *  text becomes a note (a URL becomes a link). */
  const handleDrop = async (e: React.DragEvent): Promise<void> => {
    e.preventDefault()
    setDragging(false)
    const files = Array.from(e.dataTransfer.files)
    if (files.length > 0) {
      let updated: ResearchItemDto[] | null = null
      for (const file of files) {
        const path = window.novalist.filePath(file)
        if (!path) continue
        updated = await rpc.request<ResearchItemDto[]>('research/import', [path])
      }
      if (updated) {
        setItems(updated)
        setSelectedId(updated[updated.length - 1]?.id ?? null)
      }
      return
    }

    const text = e.dataTransfer.getData('text/plain').trim()
    if (text.length === 0) return
    const isUrl = /^https?:\/\//i.test(text)
    const updated = await rpc.request<ResearchItemDto[]>('research/save', [
      null,
      isUrl ? text : t('research.titleWatermark'),
      isUrl ? 'Link' : 'Note',
      text,
      [],
      []
    ])
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

  /** Names a link item after the page it points at. Leaves the title alone when
   *  the lookup fails (offline, or the page has no title). */
  const fetchLinkTitle = async (): Promise<void> => {
    if (!selected || selected.type !== 'Link') return
    setFetchingTitle(true)
    try {
      const title = await rpc.request<string | null>('research/fetchLinkTitle', [selected.content])
      if (title) {
        patchSelected({ title })
        await save({ ...selected, title })
      }
    } finally {
      setFetchingTitle(false)
    }
  }

  /** Where an item stands and what the writer thinks of it. */
  const setLifecycle = async (
    id: string,
    status: string | null,
    rating: number | null
  ): Promise<void> => {
    setItems(await rpc.request<ResearchItemDto[]>('research/setLifecycle', [id, status, rating]))
  }

  /**
   * Links two items, both ways. A one-way link is discoverable only from the
   * item that has it, and the end worth finding is usually the other one - the
   * question a source answers is what somebody is reading when they need it.
   */
  const toggleRelated = async (otherId: string, linked: boolean): Promise<void> => {
    if (!selected) return
    setItems(
      await rpc.request<ResearchItemDto[]>('research/link', [selected.id, otherId, linked])
    )
  }

  const linkEntity = async (entityId: string): Promise<void> => {
    if (!selected || selected.entityRefs.includes(entityId)) return
    const next = { ...selected, entityRefs: [...selected.entityRefs, entityId] }
    patchSelected({ entityRefs: next.entityRefs })
    await save(next)
  }

  const unlinkEntity = async (entityId: string): Promise<void> => {
    if (!selected) return
    const next = { ...selected, entityRefs: selected.entityRefs.filter((r) => r !== entityId) }
    patchSelected({ entityRefs: next.entityRefs })
    await save(next)
  }

  /** Clears the inbox flag: the note has been dealt with and becomes an ordinary
   *  research item. Filing never deletes anything — you keep the original. */
  const markFiled = async (item: ResearchItemDto): Promise<void> => {
    const next = { ...item, tags: item.tags.filter((tag) => tag.toLowerCase() !== INBOX_TAG) }
    patchSelected({ tags: next.tags })
    await save(next)
  }

  /** Files the note onto a brand-new Codex entry named after its title. */
  const fileAsNewEntity = async (typeKey: string): Promise<void> => {
    const item = selected
    setFiling(null)
    if (!item) return
    const record = await rpc.request<Record<string, unknown>>('entities/create', [
      typeKey,
      item.title,
      null
    ])
    if (item.content.trim().length > 0) {
      await rpc.request('entities/appendToSection', [
        typeKey,
        String(record.id),
        t('capture.defaultSectionTitle'),
        item.content
      ])
    }
    await markFiled(item)
  }

  /** Files the note into an existing entry's section. */
  const fileIntoEntity = async (target: {
    typeKey: string
    id: string
    sectionTitle: string
  }): Promise<void> => {
    const item = selected
    setFiling(null)
    if (!item) return
    await rpc.request('entities/appendToSection', [
      target.typeKey,
      target.id,
      target.sectionTitle,
      item.content
    ])
    await markFiled(item)
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
        <div
          className={`codex-list${dragging ? ' research-dropping' : ''}`}
          onDragOver={(e) => {
            e.preventDefault()
            setDragging(true)
          }}
          onDragLeave={(e) => {
            if (!e.currentTarget.contains(e.relatedTarget as Node)) setDragging(false)
          }}
          onDrop={(e) => void handleDrop(e)}
        >
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
            {/* A folder of ordinary Markdown notes - which is what a vault is
                once the plugin that made it is gone, and what every other tool
                exports. */}
            <button className="research-action-btn" onClick={() => setVaultOpen(true)}>
              {t('research.importVault')}
            </button>
          </div>
          <input
            className="dialog-input research-search"
            placeholder={t('research.search')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
          {inboxCount > 0 && (
            <button
              className={`research-inbox-toggle${inboxOnly ? ' active' : ''}`}
              onClick={() => setInboxOnly((v) => !v)}
            >
              <Inbox size={13} strokeWidth={2} />
              {t('research.inbox')}
              <span className="research-inbox-count">{inboxCount}</span>
            </button>
          )}
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
                    {isInbox(item) && (
                      <span className="research-inbox-badge">{t('research.inbox')}</span>
                    )}
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
                {selected.type === 'Link' && (
                  <button
                    className="dialog-button"
                    disabled={fetchingTitle}
                    onClick={() => void fetchLinkTitle()}
                  >
                    {fetchingTitle ? t('research.fetchingTitle') : t('research.fetchTitle')}
                  </button>
                )}
                <button className="dialog-button danger" onClick={() => setConfirmDelete(true)}>
                  <Trash2 size={13} strokeWidth={2} /> {t('research.deleteItem')}
                </button>
              </div>
              {isInbox(selected) && (
                <div className="research-filing">
                  <span className="research-filing-label">{t('research.fileThis')}</span>
                  <button className="dialog-button" onClick={() => setFiling('create')}>
                    {t('research.fileAsNewEntity')}
                  </button>
                  <button className="dialog-button" onClick={() => setFiling('append')}>
                    {t('research.fileIntoEntity')}
                  </button>
                  <button className="dialog-button" onClick={() => void markFiled(selected)}>
                    {t('research.fileAsNote')}
                  </button>
                </div>
              )}
              <input
                className="dialog-input"
                placeholder={t('research.titleWatermark')}
                value={selected.title}
                onChange={(e) => patchSelected({ title: e.target.value })}
                onBlur={() => void save(selected)}
              />

              {/* Where it stands and what it is worth. A shelf of forty
                  sources has three that matter and, until now, nothing said
                  which - or which questions were still open. */}
              <div className="research-lifecycle">
                <select
                  className="dialog-input"
                  aria-label={t('research.status')}
                  value={selected.status}
                  onChange={(e) => void setLifecycle(selected.id, e.target.value, null)}
                >
                  {STATUSES.map((status) => (
                    <option key={status} value={status}>
                      {t(`research.status${status}`)}
                    </option>
                  ))}
                </select>
                <div
                  className="research-rating"
                  role="group"
                  aria-label={t('research.rating')}
                >
                  {[1, 2, 3, 4, 5].map((star) => (
                    <button
                      key={star}
                      className={`research-star${selected.rating >= star ? ' on' : ''}`}
                      aria-label={t('research.rateStars', { count: star })}
                      aria-pressed={selected.rating >= star}
                      // Clicking the star already set clears the rating, so an
                      // accidental one is one click to undo.
                      onClick={() =>
                        void setLifecycle(
                          selected.id,
                          null,
                          selected.rating === star ? 0 : star
                        )
                      }
                    >
                      <Star size={14} strokeWidth={2} />
                    </button>
                  ))}
                </div>
              </div>
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
              {/* Read, played and watched in place. The alternative is an
                  external application and a lost train of thought, which is
                  what "Open External" already is for the cases below. */}
              {selected.type === 'Pdf' && selected.content.length > 0 && (
                <object
                  className="research-embed"
                  data={`novalist-project://nl/${encodeURI(selected.content)}`}
                  type="application/pdf"
                  aria-label={selected.title}
                >
                  <p className="settings-hint">{t('research.pdfFallback')}</p>
                </object>
              )}
              {selected.type === 'Audio' && selected.content.length > 0 && (
                <audio
                  className="research-audio"
                  controls
                  src={`novalist-project://nl/${encodeURI(selected.content)}`}
                />
              )}
              {selected.type === 'Video' && selected.content.length > 0 && (
                <video
                  className="research-embed"
                  controls
                  src={`novalist-project://nl/${encodeURI(selected.content)}`}
                />
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
              <MarkdownEditor
                className="research-content"
                minRows={12}
                placeholder={t('research.contentWatermark')}
                ariaLabel={t('research.content')}
                value={selected.content}
                onChange={(next) => patchSelected({ content: next })}
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
              <div className="research-tags">
                <span className="research-tags-label">{t('research.linkedEntities')}</span>
                <div className="research-tag-list">
                  {selected.entityRefs.map((refId) => (
                    <span key={refId} className="research-tag">
                      {entityNames.get(refId) ?? refId}
                      <button
                        className="research-tag-remove"
                        aria-label={`${t('explorer.contextDelete')} ${entityNames.get(refId) ?? refId}`}
                        onClick={() => void unlinkEntity(refId)}
                      >
                        ×
                      </button>
                    </span>
                  ))}
                  {selected.entityRefs.length === 0 && (
                    <span className="research-tags-hint">{t('research.linkedEntitiesHint')}</span>
                  )}
                </div>
                <div className="research-tag-add">
                  <select
                    className="dialog-input"
                    value=""
                    onChange={(e) => {
                      if (e.target.value) void linkEntity(e.target.value)
                    }}
                  >
                    <option value="">{t('research.linkEntity')}</option>
                    {allEntities
                      .filter((e) => !selected.entityRefs.includes(e.id))
                      .map((e) => (
                        <option key={e.id} value={e.id}>
                          {e.name}
                        </option>
                      ))}
                  </select>
                </div>
              </div>
              {/* Other research this one refers to. Written both ways, because
                  the end worth finding is usually the other one: the question
                  a source answers is what somebody is reading when they need
                  the source. */}
              <div className="research-tags">
                <span className="research-tags-label">{t('research.related')}</span>
                <div className="research-tag-list">
                  {selected.relatedIds.map((relatedId) => {
                    const other = items.find((i) => i.id === relatedId)
                    return (
                      <span key={relatedId} className="research-tag">
                        <button
                          className="research-related-open"
                          onClick={() => setSelectedId(relatedId)}
                        >
                          <Link2 size={12} strokeWidth={2} /> {other?.title ?? relatedId}
                        </button>
                        <button
                          className="research-tag-remove"
                          aria-label={`${t('explorer.contextDelete')} ${other?.title ?? relatedId}`}
                          onClick={() => void toggleRelated(relatedId, false)}
                        >
                          ×
                        </button>
                      </span>
                    )
                  })}
                  {selected.relatedIds.length === 0 && (
                    <span className="research-tags-hint">{t('research.relatedHint')}</span>
                  )}
                </div>
                <div className="research-tag-add">
                  <select
                    className="dialog-input"
                    value=""
                    onChange={(e) => {
                      if (e.target.value) void toggleRelated(e.target.value, true)
                    }}
                  >
                    <option value="">{t('research.linkResearch')}</option>
                    {items
                      .filter(
                        (i) => i.id !== selected.id && !selected.relatedIds.includes(i.id)
                      )
                      .map((i) => (
                        <option key={i.id} value={i.id}>
                          {i.title}
                        </option>
                      ))}
                  </select>
                </div>
              </div>
              <CustomFieldsPanel scope="Research" id={selected.id} />

              {/* A note pasted over is as lost as a character sheet typed
                  over, and research is where a writer keeps the things they
                  cannot rewrite from memory. */}
              <div className="inspector-label">{t('entityHistory.title')}</div>
              <RevisionsPanel
                historyMethod="research/history"
                restoreMethod="research/restoreRevision"
                targetId={selected.id}
                restoreArgs={[selected.id]}
                onRestored={(updated) => setItems(updated as ResearchItemDto[])}
              />
            </div>
          ) : (
            <>
              <p className="codex-empty">{t('research.empty')}</p>
              {/* Notes captured while no project was open. Filing one moves it
                  into this project's inbox, which is where it was going to end
                  up anyway. */}
              <ScratchpadPanel
                canFile
                onFiled={() =>
                  void rpc.request<ResearchItemDto[]>('research/list').then(setItems)
                }
              />

            </>
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
      {filing === 'create' && selected && (
        <EntityTypeDialog
          name={selected.title}
          onPick={(typeKey) => void fileAsNewEntity(typeKey)}
          onCancel={() => setFiling(null)}
        />
      )}
      {filing === 'append' && selected && (
        <AppendToEntityDialog
          text={selected.content}
          onConfirm={(target) => void fileIntoEntity(target)}
          onCancel={() => setFiling(null)}
        />
      )}
      {vaultOpen && (
        <ImportVaultDialog
          onClose={() => {
            setVaultOpen(false)
            // Whatever came in should be on screen without a navigation.
            void rpc
              .request<ResearchItemDto[]>('research/list')
              .then(setItems)
              .catch(() => {})
          }}
        />
      )}
    </div>
  )
}
