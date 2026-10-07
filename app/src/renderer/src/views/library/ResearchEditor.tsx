

import { useState } from 'react'
import { RevisionsPanel } from '../../shell/RevisionsPanel'
import { MarkdownEditor } from '../../shell/MarkdownEditor'
import { ExternalLink, FolderOpen, Link2, Star, Trash2 } from 'lucide-react'

import { CustomFieldsPanel } from '../../shell/CustomFieldsPanel'

import { TYPES, STATUSES, isFileType, type ResearchItemDto, type useResearchModel } from './researchModel'
function RelatedResearch({ t, selected, items, setSelectedId, toggleRelated }: { t: ResearchViewState['t']; selected: NonNullable<ResearchViewState['selected']>; items: ResearchViewState['items']; setSelectedId: ResearchViewState['setSelectedId']; toggleRelated: ResearchViewState['toggleRelated'] }): React.JSX.Element {
  return (
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
            .filter((i) => i.id !== selected.id && !selected.relatedIds.includes(i.id))
            .map((i) => (
              <option key={i.id} value={i.id}>
                {i.title}
              </option>
            ))}
        </select>
      </div>
    </div>
  )
}

type ResearchViewState = ReturnType<typeof useResearchModel>

function ResearchEntities({ t, selected, entityNames, unlinkEntity, linkEntity, allEntities }: { t: ResearchViewState['t']; selected: NonNullable<ResearchViewState['selected']>; entityNames: ResearchViewState['entityNames']; unlinkEntity: ResearchViewState['unlinkEntity']; linkEntity: ResearchViewState['linkEntity']; allEntities: ResearchViewState['allEntities'] }): React.JSX.Element {
  return (
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
  )
}

function ResearchTags({ t, selected, removeTag, newTag, setNewTag, addTag }: { t: ResearchViewState['t']; selected: NonNullable<ResearchViewState['selected']>; removeTag: ResearchViewState['removeTag']; newTag: ResearchViewState['newTag']; setNewTag: ResearchViewState['setNewTag']; addTag: ResearchViewState['addTag'] }): React.JSX.Element {
  return (
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
  )
}

function ResearchMetadata({ t, selected, setLifecycle, patchSelected, save, removeTag, newTag, setNewTag, addTag, entityNames, unlinkEntity, linkEntity, allEntities, items, setSelectedId, toggleRelated, setItems }: { t: ResearchViewState['t']; selected: NonNullable<ResearchViewState['selected']>; setLifecycle: ResearchViewState['setLifecycle']; patchSelected: ResearchViewState['patchSelected']; save: ResearchViewState['save']; removeTag: ResearchViewState['removeTag']; newTag: ResearchViewState['newTag']; setNewTag: ResearchViewState['setNewTag']; addTag: ResearchViewState['addTag']; entityNames: ResearchViewState['entityNames']; unlinkEntity: ResearchViewState['unlinkEntity']; linkEntity: ResearchViewState['linkEntity']; allEntities: ResearchViewState['allEntities']; items: ResearchViewState['items']; setSelectedId: ResearchViewState['setSelectedId']; toggleRelated: ResearchViewState['toggleRelated']; setItems: ResearchViewState['setItems'] }): React.JSX.Element {
  return (
    <details className="research-metadata">
      <summary>{t('desktopRefresh.researchMetadata')}</summary>
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
        <div className="research-rating" role="group" aria-label={t('research.rating')}>
          {[1, 2, 3, 4, 5].map((star) => (
            <button
              key={star}
              className={`research-star${selected.rating >= star ? ' on' : ''}`}
              aria-label={t('research.rateStars', { count: star })}
              aria-pressed={selected.rating >= star}
              // Clicking the star already set clears the rating, so an
              // accidental one is one click to undo.
              onClick={() =>
                void setLifecycle(selected.id, null, selected.rating === star ? 0 : star)
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
      <ResearchTags t={t} selected={selected} removeTag={removeTag} newTag={newTag} setNewTag={setNewTag} addTag={addTag} />
      <ResearchEntities t={t} selected={selected} entityNames={entityNames} unlinkEntity={unlinkEntity} linkEntity={linkEntity} allEntities={allEntities} />
      {/* Other research this one refers to. Written both ways, because
        the end worth finding is usually the other one: the question
        a source answers is what somebody is reading when they need
        the source. */}
      <RelatedResearch t={t} selected={selected} items={items} setSelectedId={setSelectedId} toggleRelated={toggleRelated} />
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
    </details>
  )
}

export function ResearchEditor({ selected, t, fetchingTitle, fetchLinkTitle, setConfirmDelete, isInbox, setFiling, markFiled, patchSelected, save, setLifecycle, removeTag, newTag, setNewTag, addTag, entityNames, unlinkEntity, linkEntity, allEntities, items, setSelectedId, toggleRelated, setItems }: { selected: NonNullable<ResearchViewState['selected']>; t: ResearchViewState['t']; fetchingTitle: ResearchViewState['fetchingTitle']; fetchLinkTitle: ResearchViewState['fetchLinkTitle']; setConfirmDelete: ResearchViewState['setConfirmDelete']; isInbox: ResearchViewState['isInbox']; setFiling: ResearchViewState['setFiling']; markFiled: ResearchViewState['markFiled']; patchSelected: ResearchViewState['patchSelected']; save: ResearchViewState['save']; setLifecycle: ResearchViewState['setLifecycle']; removeTag: ResearchViewState['removeTag']; newTag: ResearchViewState['newTag']; setNewTag: ResearchViewState['setNewTag']; addTag: ResearchViewState['addTag']; entityNames: ResearchViewState['entityNames']; unlinkEntity: ResearchViewState['unlinkEntity']; linkEntity: ResearchViewState['linkEntity']; allEntities: ResearchViewState['allEntities']; items: ResearchViewState['items']; setSelectedId: ResearchViewState['setSelectedId']; toggleRelated: ResearchViewState['toggleRelated']; setItems: ResearchViewState['setItems'] }): React.JSX.Element {
  const [previewError, setPreviewError] = useState<{ id: string; message: string } | null>(null)
  return (
    <div className="research-editor" onErrorCapture={(event) => {
      const target = event.target as HTMLElement
      setPreviewError({ id: selected.id, message: target.getAttribute('aria-description') || t('research.previewFailed') })
    }}>
      {previewError?.id === selected.id && <p role="alert" className="settings-hint">{previewError.message}</p>}
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
        className="dialog-input research-title"
        aria-label={t('research.titleWatermark')}
        placeholder={t('research.titleWatermark')}
        value={selected.title}
        onChange={(e) => patchSelected({ title: e.target.value })}
        onBlur={() => void save(selected)}
      />

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
              {[selected.fileSize, selected.modified]
                .filter((s) => s.length > 0)
                .join(' · ')}
            </dd>
          )}
        </dl>
      )}
      <MarkdownEditor
        key={selected.id}
        className="research-content"
        minRows={12}
        placeholder={t('research.contentWatermark')}
        ariaLabel={t('research.content')}
        value={selected.content}
        onChange={(next) => patchSelected({ content: next })}
        onBlur={() => void save(selected)}
      />
      <ResearchMetadata t={t} selected={selected} setLifecycle={setLifecycle} patchSelected={patchSelected} save={save} removeTag={removeTag} newTag={newTag} setNewTag={setNewTag} addTag={addTag} entityNames={entityNames} unlinkEntity={unlinkEntity} linkEntity={linkEntity} allEntities={allEntities} items={items} setSelectedId={setSelectedId} toggleRelated={toggleRelated} setItems={setItems} />
    </div>
  )
}
