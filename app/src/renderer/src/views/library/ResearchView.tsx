import { MotionPresence } from '../../shell/MotionPresence'
import { useResearchModel, type ResearchItemDto } from './researchModel'
import { ResearchEditor } from './ResearchEditor'
import { useRef } from 'react'
import { useContentTransition } from '../../shell/useContentTransition'
import { DesktopViewActions } from '../../shell/DesktopViewFrame'

import { Inbox } from 'lucide-react'
import { rpc } from '../../rpc/client'
import { ImportFolderDialog } from '../../shell/ImportFolderDialog'
import { ScratchpadPanel } from '../../shell/ScratchpadPanel'

import { ConfirmDialog } from '../../shell/ConfirmDialog'

import { EntityTypeDialog } from '../../shell/EntityTypeDialog'
import { AppendToEntityDialog } from '../../shell/AppendToEntityDialog'
import './library.css'

export function ResearchView(): React.JSX.Element {
  const model = useResearchModel()
  const { saveError } = model
  const { t, researchTab, setResearchTab, setItems, dragging, setDragging, handleDrop, create, importFile, setVaultOpen, search, setSearch, inboxCount, inboxOnly, setInboxOnly, filtered, selectedId, setSelectedId, isInbox, selected, fetchingTitle, fetchLinkTitle, setConfirmDelete, setFiling, markFiled, patchSelected, save, setLifecycle, removeTag, newTag, setNewTag, addTag, entityNames, unlinkEntity, linkEntity, allEntities, items, toggleRelated, confirmDelete, filing, fileAsNewEntity, fileIntoEntity, vaultOpen } = model
  const contentRef = useRef<HTMLDivElement>(null)
  const detailRef = useRef<HTMLDivElement>(null)
  useContentTransition(contentRef, researchTab)
  useContentTransition(detailRef, selectedId ?? '')
  return (
    <div className="codex research-view" ref={contentRef}>
      {saveError && <p role="alert">{saveError}</p>}
      <nav className="codex-tabs" aria-label={t('shell.view.research')}>
        <button
          className={`codex-tab${researchTab === 'project' ? ' active' : ''}`}
          aria-pressed={researchTab === 'project'}
          onClick={() => setResearchTab('project')}
        >
          {t('desktopRefresh.projectResearch')}
        </button>
        <button
          className={`codex-tab${researchTab === 'scratchpad' ? ' active' : ''}`}
          aria-pressed={researchTab === 'scratchpad'}
          onClick={() => setResearchTab('scratchpad')}
        >
          {t('scratchpad.title')}
        </button>
      </nav>
      <div className="research-scratchpad" hidden={researchTab !== 'scratchpad'}>
        <ScratchpadPanel
          canFile
          active={researchTab === 'scratchpad'}
          onFiled={() => {
            void rpc.request<ResearchItemDto[]>('research/list').then(setItems)
            setResearchTab('project')
          }}
        />
      </div>
      <div className="codex-body" hidden={researchTab !== 'project'}>
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
          <DesktopViewActions>
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
              {/* The generic folder importer starts with research selected here. */}
              <button className="research-action-btn" onClick={() => setVaultOpen(true)}>
                {t('research.importVault')}
              </button>
            </div>
          </DesktopViewActions>
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
        <div className="codex-detail" ref={detailRef}>
          {selected ? (
            <ResearchEditor selected={selected} t={t} fetchingTitle={fetchingTitle} fetchLinkTitle={fetchLinkTitle} setConfirmDelete={setConfirmDelete} isInbox={isInbox} setFiling={setFiling} markFiled={markFiled} patchSelected={patchSelected} save={save} setLifecycle={setLifecycle} removeTag={removeTag} newTag={newTag} setNewTag={setNewTag} addTag={addTag} entityNames={entityNames} unlinkEntity={unlinkEntity} linkEntity={linkEntity} allEntities={allEntities} items={items} setSelectedId={setSelectedId} toggleRelated={toggleRelated} setItems={setItems} />
          ) : (
            <>
              <p className="codex-empty">{t('research.empty')}</p>
            </>
          )}
        </div>
      </div>
      <MotionPresence>{confirmDelete && selected && (
        <ConfirmDialog
          title={t('research.confirmDeleteTitle')}
          message={selected.title}
          onCancel={() => setConfirmDelete(false)}
          onConfirm={() => {
            setConfirmDelete(false)
            void rpc
              .request<ResearchItemDto[]>('research/delete', [selected.id])
              .then((updated) => {
                setItems(updated)
                setSelectedId(null)
              })
          }}
        />
      )}</MotionPresence>
      <MotionPresence>{filing === 'create' && selected && (
        <EntityTypeDialog
          name={selected.title}
          onPick={(typeKey) => void fileAsNewEntity(typeKey)}
          onCancel={() => setFiling(null)}
        />
      )}</MotionPresence>
      <MotionPresence>{filing === 'append' && selected && (
        <AppendToEntityDialog
          text={selected.content}
          onConfirm={(target) => void fileIntoEntity(target)}
          onCancel={() => setFiling(null)}
        />
      )}</MotionPresence>
      <MotionPresence>{vaultOpen && (
        <ImportFolderDialog initialTarget="research" onClose={() => setVaultOpen(false)} />
      )}</MotionPresence>
    </div>
  )
}
