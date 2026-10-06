import { type ProjectStateDto } from '../stores/projectStore'
import { rpc } from '../rpc/client'

import { InputDialog } from './InputDialog'
import { ConfirmDialog } from './ConfirmDialog'
import { ChapterDialog } from './ChapterDialog'
import { SceneDialog } from './SceneDialog'
import { StoryDateRangeDialog } from './StoryDateRangeDialog'

import { useTargetStore } from '../stores/targetStore'

import type { BinderState } from './binderState'
import type { PendingAction } from './binderTypes'

type TargetAction = Extract<PendingAction, { kind: 'sceneTarget' | 'chapterTarget' | 'actTarget' }>
type ChapterPropertyAction = Extract<PendingAction, { kind: 'chapterDescription' | 'setAct' }>

async function applyWritingTarget(action: TargetAction, value: string): Promise<void> {
  const target = Number(value) || null
  const targets = useTargetStore.getState()
  switch (action.kind) {
    case 'sceneTarget':
      for (const scene of action.targets) {
        await targets.setScene(scene.chapterGuid, scene.sceneId, target)
      }
      break
    case 'chapterTarget':
      await targets.setChapter(action.chapterGuid, target)
      break
    case 'actTarget':
      await targets.setAct(action.actName, target)
      break
  }
}

function applyChapterProperty(action: ChapterPropertyAction, value: string): Promise<ProjectStateDto> {
  return action.kind === 'chapterDescription'
    ? rpc.request<ProjectStateDto>('project/setChapterDescription', [action.chapterGuid, value])
    : rpc.request<ProjectStateDto>('project/setChapterAct', [action.chapterGuid, value])
}

export function BinderDialogs({ state }: { state: BinderState }): React.JSX.Element {
  const { t, addChapterOpen, setAddChapterOpen, addSceneChapter, setAddSceneChapter, pending, setPending, chapters, store } = state
  return <>
      {addChapterOpen && <ChapterDialog onClose={() => setAddChapterOpen(false)} />}
      {addSceneChapter && (
        <SceneDialog
          defaultChapterGuid={addSceneChapter}
          onClose={() => setAddSceneChapter(null)}
        />
      )}
      {pending?.kind === 'editChapter' &&
        chapters.some((c) => c.guid === pending.chapterGuid) && (
          <ChapterDialog
            chapter={chapters.find((c) => c.guid === pending.chapterGuid)}
            onClose={() => setPending(null)}
          />
        )}
      {pending?.kind === 'sceneTemplate' && (
        <InputDialog
          title={t('explorer.templateNameTitle')}
          placeholder={t('explorer.templateNamePlaceholder')}
          initialValue={pending.title}
          onCancel={() => setPending(null)}
          onSubmit={(name) => {
            const target = pending
            setPending(null)
            void rpc.request('sceneTemplates/saveFromScene', [
              target.chapterGuid,
              target.sceneId,
              name
            ])
          }}
        />
      )}
      {pending?.kind === 'editScene' && (
        <SceneDialog
          edit={{
            chapterGuid: pending.chapterGuid,
            sceneId: pending.sceneId,
            title: pending.current
          }}
          onClose={() => setPending(null)}
        />
      )}
      {pending?.kind === 'setDate' && (
        <StoryDateRangeDialog
          chapterGuid={pending.chapterGuid}
          sceneId={pending.sceneId}
          onClose={() => setPending(null)}
        />
      )}
      {(pending?.kind === 'sceneTarget' || pending?.kind === 'chapterTarget' || pending?.kind === 'actTarget') && (
        <InputDialog
          title={t('targets.prompt')}
          placeholder={pending.current}
          onCancel={() => setPending(null)}
          onSubmit={(value) => {
            const action = pending
            setPending(null)
            void applyWritingTarget(action, value)
          }}
        />
      )}
      {pending?.kind === 'insertChapter' && (
        <InputDialog
          title={t('explorer.insertChapterTitle')}
          placeholder={t('shell.newChapter')}
          onCancel={() => setPending(null)}
          onSubmit={(title) => {
            const at = pending.beforeOrder
            setPending(null)
            if (title.trim().length > 0) void store.getState().createChapter(title.trim(), at)
          }}
        />
      )}
      {(pending?.kind === 'chapterDescription' || pending?.kind === 'setAct') && (
        <InputDialog
          title={t(pending.kind === 'chapterDescription' ? 'explorer.chapterDescription' : 'explorer.renameAct')}
          placeholder={pending.current}
          onCancel={() => setPending(null)}
          onSubmit={(value) => {
            const action = pending
            setPending(null)
            void applyChapterProperty(action, value).then((project) => store.getState().applyState(project))
          }}
        />
      )}
      {(pending?.kind === 'deleteChapter' || pending?.kind === 'deleteScene') && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={t(pending.kind === 'deleteChapter' ? 'explorer.confirmDeleteChapter' : 'explorer.confirmDeleteScene', { name: pending.title })}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            setPending(null)
            if (pending.kind === 'deleteChapter') {
              void store.getState().deleteChapter(pending.chapterGuid)
            } else {
              void store.getState().deleteScene(pending.chapterGuid, pending.sceneId)
            }
          }}
        />
      )}
      {pending?.kind === 'deleteScenes' && (
        <ConfirmDialog
          title={t('explorer.deleteTitle')}
          message={t('bulk.confirmDelete', { count: pending.targets.length })}
          onCancel={() => setPending(null)}
          onConfirm={() => {
            const ids = pending.targets.map((target) => target.sceneId)
            setPending(null)
            void store.getState().mutateSceneStructure('sceneBulk/delete', [ids])
          }}
        />
      )}
  </>
}
