import { Link2, Star } from 'lucide-react'
import { RevisionsPanel } from '../../shell/RevisionsPanel'
import { CustomFieldsPanel } from '../../shell/CustomFieldsPanel'
import { metadataBinding, SharedMetadataField } from '../../collaboration/SharedMetadataField'
import { TYPES, STATUSES, type ResearchItemDto, type useResearchModel } from './researchModel'

function RelatedResearch({ t, selected, items, setSelectedId, toggleRelated }: { t: ResearchViewState['t']; selected: NonNullable<ResearchViewState['selected']>; items: ResearchViewState['items']; setSelectedId: ResearchViewState['setSelectedId']; toggleRelated: ResearchViewState['toggleRelated'] }): React.JSX.Element {
  return <SharedMetadataField<string[] | null> binding={metadataBinding(selected, 'relatedIds')} value={selected.relatedIds} onChange={value => {
    const next = value ?? []
    const removed = selected.relatedIds.find(id => !next.includes(id))
    const added = next.find(id => !selected.relatedIds.includes(id))
    if (removed) void toggleRelated(removed, false)
    if (added) void toggleRelated(added, true)
  }}>{field => (
    <div className="research-tags">
      <span className="research-tags-label">{t('research.related')}</span>
      <div className="research-tag-list">
        {(field.value ?? []).map((relatedId) => {
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
                aria-disabled={!field.editable}
                onClick={() => field.change(ids => (ids ?? []).filter(id => id !== relatedId))}
              >
                ×
              </button>
            </span>
          )
        })}
        {!field.value?.length && (
          <span className="research-tags-hint">{t('research.relatedHint')}</span>
        )}
      </div>
      <div className="research-tag-add">
        <select
          className="dialog-input"
          value=""
          aria-label={t('research.linkResearch')}
          aria-disabled={!field.editable}
          onKeyDown={event => { if (!field.editable && event.key !== 'Tab') event.preventDefault() }}
          onChange={(e) => {
            const id = e.target.value
            if (id) field.change(ids => (ids ?? []).includes(id) ? ids : [...(ids ?? []), id])
          }}
        >
          <option value="">{t('research.linkResearch')}</option>
          {items
            .filter((i) => i.id !== selected.id && !field.value?.includes(i.id))
            .map((i) => (
              <option key={i.id} value={i.id}>
                {i.title}
              </option>
            ))}
        </select>
      </div>
    </div>
  )}</SharedMetadataField>
}

type ResearchViewState = ReturnType<typeof useResearchModel>

function ResearchEntities({ t, selected, entityNames, allEntities, patchSelected, save }: Pick<ResearchViewState, 't' | 'entityNames' | 'allEntities' | 'patchSelected' | 'save'> & { selected: ResearchItemDto }): React.JSX.Element {
  return <SharedMetadataField binding={metadataBinding(selected, 'entityRefs')} value={selected.entityRefs} onChange={entityRefs => {
    patchSelected({ entityRefs })
    void save({ ...selected, entityRefs })
  }}>{field => (
    <div className="research-tags">
      <span className="research-tags-label">{t('research.linkedEntities')}</span>
      <div className="research-tag-list">
        {field.value.map((refId) => (
          <span key={refId} className="research-tag">
            {entityNames.get(refId) ?? refId}
            <button
              className="research-tag-remove"
              aria-label={`${t('explorer.contextDelete')} ${entityNames.get(refId) ?? refId}`}
              aria-disabled={!field.editable}
              onClick={() => field.change(ids => ids.filter(id => id !== refId))}
            >
              ×
            </button>
          </span>
        ))}
        {field.value.length === 0 && (
          <span className="research-tags-hint">{t('research.linkedEntitiesHint')}</span>
        )}
      </div>
      <div className="research-tag-add">
        <select
          className="dialog-input"
          value=""
          aria-disabled={!field.editable}
          aria-label={t('research.linkEntity')}
          onKeyDown={event => { if (!field.editable && event.key !== 'Tab') event.preventDefault() }}
          onChange={(e) => {
            const id = e.target.value
            if (id) field.change(ids => ids.includes(id) ? ids : [...ids, id])
          }}
        >
          <option value="">{t('research.linkEntity')}</option>
          {allEntities
            .filter((e) => !field.value.includes(e.id))
            .map((e) => (
              <option key={e.id} value={e.id}>
                {e.name}
              </option>
            ))}
        </select>
      </div>
    </div>
  )}</SharedMetadataField>
}

function ResearchTags({ t, selected, newTag, setNewTag, patchSelected, save }: Pick<ResearchViewState, 't' | 'newTag' | 'setNewTag' | 'patchSelected' | 'save'> & { selected: ResearchItemDto }): React.JSX.Element {
  return <SharedMetadataField binding={metadataBinding(selected, 'tags')} value={selected.tags} onChange={tags => {
    patchSelected({ tags })
    void save({ ...selected, tags })
  }}>{field => {
    const addTag = (): void => {
      const tag = newTag.trim()
      if (!tag || !field.editable) return
      field.change(tags => tags.some(existing => existing.toLowerCase() === tag.toLowerCase()) ? tags : [...tags, tag])
      setNewTag('')
    }
    return (
    <div className="research-tags">
      <span className="research-tags-label">{t('research.tags')}</span>
      <div className="research-tag-list">
        {field.value.map((tag) => (
          <span key={tag} className="research-tag">
            {tag}
            <button
              className="research-tag-remove"
              aria-label={`${t('explorer.contextDelete')} ${tag}`}
              aria-disabled={!field.editable}
              onClick={() => field.change(tags => tags.filter(existing => existing !== tag))}
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
          readOnly={!field.editable}
          onChange={(e) => setNewTag(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') addTag()
          }}
        />
        <button className="dialog-button" aria-disabled={!field.editable} onClick={addTag}>
          +
        </button>
      </div>
    </div>
    )
  }}</SharedMetadataField>
}

export function ResearchMetadata({ t, selected, setLifecycle, patchSelected, save, newTag, setNewTag, entityNames, allEntities, items, setSelectedId, toggleRelated, setItems }: { t: ResearchViewState['t']; selected: NonNullable<ResearchViewState['selected']>; setLifecycle: ResearchViewState['setLifecycle']; patchSelected: ResearchViewState['patchSelected']; save: ResearchViewState['save']; removeTag: ResearchViewState['removeTag']; newTag: ResearchViewState['newTag']; setNewTag: ResearchViewState['setNewTag']; addTag: ResearchViewState['addTag']; entityNames: ResearchViewState['entityNames']; unlinkEntity: ResearchViewState['unlinkEntity']; linkEntity: ResearchViewState['linkEntity']; allEntities: ResearchViewState['allEntities']; items: ResearchViewState['items']; setSelectedId: ResearchViewState['setSelectedId']; toggleRelated: ResearchViewState['toggleRelated']; setItems: ResearchViewState['setItems'] }): React.JSX.Element {
  return (
    <details className="research-metadata">
      <summary>{t('desktopRefresh.researchMetadata')}</summary>
      {/* Where it stands and what it is worth. A shelf of forty
        sources has three that matter and, until now, nothing said
        which - or which questions were still open. */}
      <div className="research-lifecycle">
        <SharedMetadataField binding={metadataBinding(selected, 'status')} value={selected.status}
          onChange={status => void setLifecycle(selected.id, status, null)}>{field => <select
          className="dialog-input"
          aria-label={t('research.status')}
          aria-disabled={!field.editable}
          value={field.value}
          onKeyDown={event => { if (!field.editable && event.key !== 'Tab') event.preventDefault() }}
          onChange={event => field.change(event.target.value)}
        >
          {STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(`research.status${status}`)}
            </option>
          ))}
        </select>}</SharedMetadataField>
        <SharedMetadataField binding={metadataBinding(selected, 'rating')} value={selected.rating}
          onChange={rating => void setLifecycle(selected.id, null, rating)}>{field => <div className="research-rating" role="group" aria-label={t('research.rating')}>
          {[1, 2, 3, 4, 5].map((star) => (
            <button
              key={star}
              className={`research-star${field.value >= star ? ' on' : ''}`}
              aria-label={t('research.rateStars', { count: star })}
              aria-pressed={field.value >= star}
              aria-disabled={!field.editable}
              // Clicking the star already set clears the rating, so an
              // accidental one is one click to undo.
              onClick={() =>
                field.change(rating => rating === star ? 0 : star)
              }
            >
              <Star size={14} strokeWidth={2} />
            </button>
          ))}
        </div>}</SharedMetadataField>
      </div>
      <SharedMetadataField binding={metadataBinding(selected, 'type')} value={selected.type} onChange={type => {
        patchSelected({ type })
        void save({ ...selected, type })
      }}>{field => <select
        className="dialog-input"
        value={field.value}
        aria-disabled={!field.editable}
        onKeyDown={event => { if (!field.editable && event.key !== 'Tab') event.preventDefault() }}
        onChange={event => field.change(event.target.value)}
      >
        {TYPES.map((type) => (
          <option key={type} value={type}>
            {t(`research.type${type}`, { defaultValue: type })}
          </option>
        ))}
      </select>}</SharedMetadataField>
      <ResearchTags t={t} selected={selected} newTag={newTag} setNewTag={setNewTag} patchSelected={patchSelected} save={save} />
      <ResearchEntities t={t} selected={selected} entityNames={entityNames} allEntities={allEntities} patchSelected={patchSelected} save={save} />
      {/* Other research this one refers to. Written both ways, because
        the end worth finding is usually the other one: the question
        a source answers is what somebody is reading when they need
        the source. */}
      <RelatedResearch t={t} selected={selected} items={items} setSelectedId={setSelectedId} toggleRelated={toggleRelated} />
      <CustomFieldsPanel scope="Research" id={selected.id} sharedFields={selected.sharedFields} />

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

