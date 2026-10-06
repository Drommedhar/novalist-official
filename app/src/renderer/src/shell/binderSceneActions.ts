import { type ProjectStateDto } from '../stores/projectStore'
import { rpc } from '../rpc/client'
import { type ContextMenuItem } from './ContextMenu'

import { useShellStore } from '../stores/shellStore'

import { useTargetStore } from '../stores/targetStore'

import type { SceneMenuContext } from './binderSceneMenuContext'
export function sceneMergeItems(context: SceneMenuContext): ContextMenuItem[] {
  const { chapter, scene, sceneIndex, state } = context
  const { t, store } = state
  return (sceneIndex < chapter.scenes.length - 1
            ? [
                {
                  label: t('splitMerge.mergeWithNext', {
                    title: chapter.scenes[sceneIndex + 1].title
                  }),
                  onClick: () => {
                    const next = chapter.scenes[sceneIndex + 1]
                    void rpc
                      .request<{
                        sceneId: string | null
                        state: import('../stores/projectStore').ProjectStateDto
                      }>('sceneSplit/merge', [chapter.guid, scene.id, next.id])
                      .then((result) => {
                        store.getState().applyState(result.state)
                        // The merged scene grew, so whatever the editor is
                        // showing of it is now short.
                        void store.getState().openScene(chapter.guid, scene.id)
                      })
                  }
                }
              ]
            : [])
}

export function sceneExportItems(context: SceneMenuContext): ContextMenuItem[] {
  const { scene, targets, scoped, state } = context
  const { t, setPending, store } = state
  return [

          {
            label: scoped(t('targets.setScene')),
            onClick: () =>
              setPending({
                kind: 'sceneTarget',
                targets,
                current: String(useTargetStore.getState().find('scene', scene.id)?.target ?? '')
              })
          },

          {
            // Reads as what it does to the book, not as a field being set: the
            // scene stays in the binder either way.
            label: scoped(
              scene.excludeFromExport ? t('export.includeScene') : t('export.excludeScene')
            ),
            onClick: () => {
              void rpc
                .request<{ state: import('../stores/projectStore').ProjectStateDto }>(
                  'sceneBulk/setExportInclusion',
                  [targets.map((target) => target.sceneId), scene.excludeFromExport]
                )
                .then((result) => store.getState().applyState(result.state))
            }
          }
  ]
}

export function sceneBookmarkItems(context: SceneMenuContext): ContextMenuItem[] {
  const { chapter, scene, state } = context
  const { t, togglePin } = state
  return [

          {
            // Top of the binder, whatever the sort and wherever the chapter. The
            // flag has been on the model and saved to disk for years with no way
            // for a writer to set it.
            label: scene.isFavorite ? t('binder.unpin') : t('binder.pin'),
            onClick: () => togglePin(chapter.guid, scene.id, !scene.isFavorite)
          },

          {
            // A place worth coming back to, which is a different question from
            // "which scenes match this query" - the one saved lists answer.
            label: t('bookmarks.addScene'),
            onClick: () => {
              void rpc
                .request('bookmarks/save', [
                  {
                    id: '',
                    kind: 'Scene',
                    label: scene.title,
                    group: '',
                    chapterGuid: chapter.guid,
                    targetId: scene.id,
                    targetType: '',
                    anchorText: '',
                    storyDate: '',
                    order: 0
                  }
                ])
                .then(() => useShellStore.getState().setBinderTab('bookmarks'))
            }
          }
  ]
}

export function sceneActivityItems(context: SceneMenuContext): ContextMenuItem[] {
  const { scene, targets, scoped, state } = context
  const { t, store } = state
  return [

          {
            // Out of the book, still in the plan. The step between keeping a
            // scene and archiving it, which until now was the only way down.
            label: scoped(scene.inactive ? t('scene.makeActive') : t('scene.makeInactive')),
            onClick: () => {
              void Promise.all(
                targets.map((target) =>
                  rpc.request('scenes/setInactive', [
                    target.chapterGuid,
                    target.sceneId,
                    !scene.inactive
                  ])
                )
              )
                .then(() => rpc.request<ProjectStateDto>('project/getState'))
                .then((state) => store.getState().applyState(state))
            }
          }
  ]
}

export function sceneLifecycleItems(context: SceneMenuContext): ContextMenuItem[] {
  const { chapter, scene, targets, scoped, state } = context
  const { t, setPending, store, archiveOpen, loadArchived } = state
  return [

          {
            // Made from a scene that already reads right, rather than described
            // in a form: pointing at one is easier than writing down what it is.
            label: t('explorer.saveAsTemplate'),
            onClick: () =>
              setPending({
                kind: 'sceneTemplate',
                chapterGuid: chapter.guid,
                sceneId: scene.id,
                title: scene.title
              })
          },

          {
            label: scoped(t('explorer.contextArchive')),
            onClick: () => {
              void store.getState()
                .mutateSceneStructure('sceneBulk/archive', [targets.map((target) => target.sceneId)])
                .then(() => {
                  if (archiveOpen) void loadArchived()
                })
            }
          },

          {
            label: t('menu.toggleSplitEditor'),
            onClick: () => void store.getState().openSceneInSplit(chapter.guid, scene.id)
          },

          {
            label: t('explorer.renameScene'),
            onClick: () =>
              setPending({
                kind: 'editScene',
                chapterGuid: chapter.guid,
                sceneId: scene.id,
                current: scene.title
              })
          },

          {
            label: t('explorer.contextSetDate'),
            onClick: () =>
              setPending({ kind: 'setDate', chapterGuid: chapter.guid, sceneId: scene.id })
          },

          {
            label: scoped(t('explorer.contextDelete')),
            danger: true,
            onClick: () =>
              setPending(
                targets.length > 1
                  ? { kind: 'deleteScenes', targets }
                  : {
                      kind: 'deleteScene',
                      chapterGuid: chapter.guid,
                      sceneId: scene.id,
                      title: scene.title
                    }
              )
          }
  ]
}
