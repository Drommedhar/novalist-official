import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { rpc } from '../rpc/client'
import { type ContextMenuItem } from './ContextMenu'

import { useStageStore } from '../stores/stageStore'

import type { SceneMenuContext } from './binderSceneMenuContext'
export function sceneLabelItems(context: SceneMenuContext): ContextMenuItem[] {
  const { targets, scoped, state } = context
  const { t } = state
  // One entry per label the book defines, plus a way back to none.
        const labels = state.labelList
  const labelItems: ContextMenuItem[] = [
          ...labels.map((label) => ({
            label: scoped(`${t('labels.set')}: ${label.label}`),
            onClick: () => {
              void (async () => {
                for (const target of targets)
                  await rpc.request('labels/setScene', [target.sceneId, label.key])
                useProjectStore
                  .getState()
                  .applyState(await rpc.request<ProjectStateDto>('project/getState'))
              })()
            }
          })),
          ...(labels.length > 0
            ? [
                {
                  label: scoped(t('labels.none')),
                  onClick: () => {
                    void (async () => {
                      for (const target of targets)
                        await rpc.request('labels/setScene', [target.sceneId, null])
                      useProjectStore
                        .getState()
                        .applyState(await rpc.request<ProjectStateDto>('project/getState'))
                    })()
                  }
                }
              ]
            : [])
        ]
  return labelItems
}

export function sceneStageItems(context: SceneMenuContext): ContextMenuItem[] {
  const { targets, scoped, state } = context
  const { t, chapters } = state
  // One entry per stage, plus a way back to untriaged. A submenu would be
        // better, but ContextMenu is a flat list and prefixing keeps it readable.
        const stageItems: ContextMenuItem[] = useStageStore.getState().stages.map((stage) => ({
          label: scoped(`${t('stages.setTo')}: ${stage.label}`),
          onClick: () => {
            void (async () => {
              for (const target of targets) {
                await useStageStore
                  .getState()
                  .setSceneStage(target.chapterGuid, target.sceneId, stage.key)
              }
            })()
          }
        }))
        if (targets.some((target) => chapters.some((c) =>
          c.scenes.some((sc) => sc.id === target.sceneId && sc.stage))))
        {
          stageItems.push({
            label: scoped(t('stages.clear')),
            onClick: () => {
              void (async () => {
                for (const target of targets) {
                  await useStageStore
                    .getState()
                    .setSceneStage(target.chapterGuid, target.sceneId, null)
                }
              })()
            }
          })
        }
  return stageItems
}
