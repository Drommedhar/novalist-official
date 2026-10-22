import { useState } from 'react'
import { ExternalLink, FolderOpen, Trash2 } from 'lucide-react'
import { MarkdownEditor } from '../../shell/MarkdownEditor'
import { metadataBinding, SharedMetadataField } from '../../collaboration/SharedMetadataField'
import { isFileType, type useResearchModel } from './researchModel'
import { ResearchMetadata } from './ResearchMetadata'

type ResearchViewState = ReturnType<typeof useResearchModel>

export function ResearchEditor({ selected, t, fetchingTitle, fetchLinkTitle, setConfirmDelete, isInbox, setFiling, markFiled, patchSelected, save, setLifecycle, removeTag, newTag, setNewTag, addTag, entityNames, unlinkEntity, linkEntity, allEntities, items, setSelectedId, toggleRelated, setItems }: { selected: NonNullable<ResearchViewState['selected']>; t: ResearchViewState['t']; fetchingTitle: ResearchViewState['fetchingTitle']; fetchLinkTitle: ResearchViewState['fetchLinkTitle']; setConfirmDelete: ResearchViewState['setConfirmDelete']; isInbox: ResearchViewState['isInbox']; setFiling: ResearchViewState['setFiling']; markFiled: ResearchViewState['markFiled']; patchSelected: ResearchViewState['patchSelected']; save: ResearchViewState['save']; setLifecycle: ResearchViewState['setLifecycle']; removeTag: ResearchViewState['removeTag']; newTag: ResearchViewState['newTag']; setNewTag: ResearchViewState['setNewTag']; addTag: ResearchViewState['addTag']; entityNames: ResearchViewState['entityNames']; unlinkEntity: ResearchViewState['unlinkEntity']; linkEntity: ResearchViewState['linkEntity']; allEntities: ResearchViewState['allEntities']; items: ResearchViewState['items']; setSelectedId: ResearchViewState['setSelectedId']; toggleRelated: ResearchViewState['toggleRelated']; setItems: ResearchViewState['setItems'] }): React.JSX.Element {
  const [previewError, setPreviewError] = useState<{ id: string; message: string } | null>(null)
  const titleBinding = metadataBinding(selected, 'title')
  const contentBinding = metadataBinding(selected, 'content')
  const previewLoaded = (event: React.SyntheticEvent): void => {
    const target = event.target as HTMLElement
    if (!target.matches('.research-preview img, .research-embed, .research-audio')) return
    if (window.novalist.isMobile) {
      target.removeAttribute('aria-description')
      target.removeAttribute('title')
    }
    setPreviewError(null)
  }
  return (
    <div className="research-editor" onLoadCapture={previewLoaded} onLoadedDataCapture={previewLoaded} onErrorCapture={(event) => {
      const target = event.target as HTMLElement
      const reason = target.getAttribute('aria-description')
      const source = target.getAttribute(target.tagName === 'OBJECT' ? 'data' : 'src')
      // WebKit may reject the project URL before the mobile host resolves it.
      if (window.novalist.isMobile && !reason && source?.startsWith('novalist-project://')) return
      setPreviewError({ id: selected.id, message: reason || t('research.previewFailed') })
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
      <SharedMetadataField binding={titleBinding} value={selected.title} onChange={title => patchSelected({ title })}>
        {field => <input
          className="dialog-input research-title"
          aria-label={t('research.titleWatermark')}
          placeholder={t('research.titleWatermark')}
          value={field.value}
          readOnly={!field.editable}
          onChange={event => field.change(event.target.value)}
          onBlur={() => { if (!titleBinding) void save(selected) }}
        />}
      </SharedMetadataField>

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
      <SharedMetadataField binding={contentBinding} value={selected.content} onChange={content => patchSelected({ content })}>{field => <MarkdownEditor
        key={selected.id}
        className="research-content"
        minRows={12}
        placeholder={t('research.contentWatermark')}
        ariaLabel={t('research.content')}
        value={field.value}
        readOnly={!field.editable}
        shared={selected.sharedProse}
        onChange={field.change}
        onBlur={() => { if (!selected.sharedProse && !contentBinding) void save(selected) }}
      />}</SharedMetadataField>
      <ResearchMetadata t={t} selected={selected} setLifecycle={setLifecycle} patchSelected={patchSelected} save={save} removeTag={removeTag} newTag={newTag} setNewTag={setNewTag} addTag={addTag} entityNames={entityNames} unlinkEntity={unlinkEntity} linkEntity={linkEntity} allEntities={allEntities} items={items} setSelectedId={setSelectedId} toggleRelated={toggleRelated} setItems={setItems} />
    </div>
  )
}
