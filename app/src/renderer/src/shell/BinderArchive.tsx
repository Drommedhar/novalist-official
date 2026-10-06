import { rpc } from '../rpc/client'

import type { BinderState } from './binderState'

export function BinderArchive({ state }: { state: BinderState }): React.JSX.Element {
  const { t, binderTab, chapters, store, archiveOpen, setArchiveOpen, archived, trashed, restoreInto, setRestoreInto, loadArchived } = state
  return <>
        {binderTab === 'chapters' && (
          <div className="binder-archived">
            <button
              className="binder-group-label binder-archived-toggle"
              onClick={() => setArchiveOpen((open) => !open)}
            >
              {t('explorer.archive')}
            </button>
            {archiveOpen && trashed.length > 0 && (
              <div className="binder-trash-chapters">
                {trashed.map((chapter) => (
                  <div key={chapter.guid} className="binder-scene-row">
                    <span className="binder-scene-title" title={chapter.title}>
                      {chapter.title}
                      <span className="binder-trash-meta">
                        {' '}
                        {t('explorer.trashScenes', { count: chapter.sceneCount })}
                      </span>
                    </span>
                    <button
                      className="snapshot-restore"
                      onClick={() => {
                        void rpc
                          .request<import('../stores/projectStore').ProjectStateDto>(
                            'project/restoreChapter',
                            [chapter.guid]
                          )
                          .then((state) => {
                            store.getState().applyState(state)
                            void loadArchived()
                          })
                      }}
                    >
                      {t('snapshots.restore')}
                    </button>
                    <button
                      className="snapshot-restore"
                      onClick={() => {
                        // The only action in the binder that destroys anything.
                        if (!window.confirm(t('explorer.purgeConfirm', { title: chapter.title })))
                          return
                        void rpc
                          .request('project/purgeChapter', [chapter.guid])
                          .then(() => loadArchived())
                      }}
                    >
                      {t('explorer.purge')}
                    </button>
                  </div>
                ))}
              </div>
            )}
            {archiveOpen && archived.length > 0 && chapters.length > 0 && (
              <label className="binder-restore-target">
                {t('explorer.restoreInto')}
                <select
                  className="inspector-input"
                  value={restoreInto}
                  onChange={(e) => setRestoreInto(e.target.value)}
                >
                  {/* The default, and what a writer restoring something almost
                      always means. Every restore used to land in chapter one
                      wherever the scene came from. */}
                  <option value="">{t('explorer.restoreHome')}</option>
                  {chapters.map((c) => (
                    <option key={c.guid} value={c.guid}>
                      {c.title}
                    </option>
                  ))}
                </select>
              </label>
            )}
            {archiveOpen && archived.map((scene) => (
              <div key={scene.id} className="binder-scene-row">
                <span className="binder-scene-title" title={scene.title}>{scene.title}</span>
                {scene.originChapterTitle && (
                  <span className="binder-pin-chapter">{scene.originChapterTitle}</span>
                )}
                <button
                  className="snapshot-restore"
                  onClick={() => {
                    void rpc
                      .request('scenes/restoreArchived', [scene.id, restoreInto || null])
                      .then(async () => {
                        const state = await rpc.request<
                          import('../stores/projectStore').ProjectStateDto
                        >('project/getState')
                        store.getState().applyState(state)
                        void loadArchived()
                      })
                  }}
                >
                  {t('snapshots.restore')}
                </button>
              </div>
            ))}
            {archiveOpen && archived.length === 0 && trashed.length === 0 && (
              <div className="binder-placeholder">{t('explorer.archiveEmpty')}</div>
            )}
          </div>
        )}
  </>
}
