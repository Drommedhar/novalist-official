import { type ContextMenuItem } from './ContextMenu'

import { useShellStore } from '../stores/shellStore'

import { useTargetStore } from '../stores/targetStore'

import type { BinderState } from './binderState'

import { reorderItems } from './binderMenuMovement'
export function chapterMenuItems(state: BinderState, chapter: BinderState['chapters'][number]): ContextMenuItem[] {
  const { t, isMobile, chapters, store, setPending } = state

    const chapterIndex = chapters.findIndex((c) => c.guid === chapter.guid)
  const chapterMoves = reorderItems(state, chapterIndex, chapters.map((item) => item.order),
    (order) => { void store.getState().reorderChapter(chapter.guid, order) })

    return [
      ...chapterMoves,
      ...(isMobile ? [{
        label: t('export.exportChapter'),
        onClick: () => useShellStore.getState().openExport(chapter.guid)
      }] : []),
      {
        // Without this the only way to put a chapter mid-book was to append it
        // and drag it up past everything after it - a dozen drags on a long
        // book, each one a save.
        label: t('explorer.insertChapterBefore'),
        onClick: () => setPending({ kind: 'insertChapter', beforeOrder: chapter.order })
      },
      {
        label: t('explorer.insertChapterAfter'),
        onClick: () => setPending({ kind: 'insertChapter', beforeOrder: chapter.order + 1 })
      },
      {
        // What the chapter is for, in the writer's words. Distinct from the
        // subtitle, which is what a reader sees.
        label: t('explorer.chapterDescription'),
        onClick: () =>
          setPending({
            kind: 'chapterDescription',
            chapterGuid: chapter.guid,
            current: chapter.description ?? ''
          })
      },
      {
        label: t('explorer.renameAct'),
        onClick: () =>
          setPending({ kind: 'setAct', chapterGuid: chapter.guid, current: chapter.act })
      },
      {
        label: t('targets.setChapter'),
        onClick: () =>
          setPending({
            kind: 'chapterTarget',
            chapterGuid: chapter.guid,
            current: String(
              useTargetStore.getState().find('chapter', chapter.guid)?.explicit
                ? (useTargetStore.getState().find('chapter', chapter.guid)?.target ?? '')
                : ''
            )
          })
      },
      // Only offered where an act exists to set it on.
      ...(chapter.act
        ? [
            {
              label: t('targets.setAct', { act: chapter.act }),
              onClick: () =>
                setPending({
                  kind: 'actTarget',
                  actName: chapter.act,
                  current: String(
                    useTargetStore.getState().find('act', chapter.act)?.explicit
                      ? (useTargetStore.getState().find('act', chapter.act)?.target ?? '')
                      : ''
                  )
                })
            }
          ]
        : []),
      {
        label: t('explorer.renameChapter'),
        onClick: () => setPending({ kind: 'editChapter', chapterGuid: chapter.guid })
      },
      {
        label: t('explorer.contextDelete'),
        danger: true,
        onClick: () =>
          setPending({ kind: 'deleteChapter', chapterGuid: chapter.guid, title: chapter.title })
      }
    ]
}
