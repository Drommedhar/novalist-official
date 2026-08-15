import { useTranslation } from 'react-i18next'
import { ChapterDialog } from './ChapterDialog'
import { ConfirmDialog } from './ConfirmDialog'
import { CreateProjectDialog } from './CreateProjectDialog'
import { DraftCompareDialog } from './DraftCompareDialog'
import { ImportManuscriptDialog } from './ImportManuscriptDialog'
import { ImportPluginDialog } from './ImportPluginDialog'
import { InputDialog } from './InputDialog'
import { PaneLayoutsDialog } from './PaneLayoutsDialog'
import { SceneDialog } from './SceneDialog'
import { SnapshotsDialog } from './SnapshotsDialog'
import { rpc } from '../rpc/client'
import { useProjectStore, type ProjectStateDto } from '../stores/projectStore'
import { useShellStore } from '../stores/shellStore'

/**
 * Every dialog the shell owns, raised by whichever surface asked for it.
 *
 * They used to be local state inside the toolbar button that opened them,
 * which meant a dialog existed only for as long as one button was on screen
 * and could be reached only by pressing it. "New chapter" could not be run
 * from the command palette or a menu, and the snapshot history disappeared
 * along with the toolbar it lived on whenever the window got narrow.
 *
 * One host, one flag in the store, and any surface can name any of them.
 */
export function ShellDialogs(): React.JSX.Element | null {
  const { t } = useTranslation()
  const dialog = useShellStore((s) => s.dialog)
  const close = useShellStore((s) => s.closeDialog)
  const projectName = useProjectStore((s) => s.projectName)
  const chapters = useProjectStore((s) => s.chapters)
  const drafts = useProjectStore((s) => s.drafts)
  const openChapterGuid = useProjectStore((s) => s.openChapterGuid)
  const openSceneId = useProjectStore((s) => s.openSceneId)

  if (dialog === null) return null

  const targetChapter = openChapterGuid ?? chapters[chapters.length - 1]?.guid ?? null
  const activeDraft = drafts.find((d) => d.isActive) ?? null

  if (dialog === 'chapter') return <ChapterDialog onClose={close} />

  if (dialog === 'scene') {
    // A book with no chapters has nowhere to put a scene. The command is
    // unavailable in that state, so this is the belt to its braces.
    return targetChapter === null ? null : (
      <SceneDialog defaultChapterGuid={targetChapter} onClose={close} />
    )
  }

  if (dialog === 'snapshots') {
    return openChapterGuid && openSceneId ? (
      <SnapshotsDialog chapterGuid={openChapterGuid} sceneId={openSceneId} onClose={close} />
    ) : null
  }

  if (dialog === 'draftCompare') return <DraftCompareDialog onClose={close} />

  if (dialog === 'deleteDraft') {
    return activeDraft === null || drafts.length <= 1 ? null : (
      <ConfirmDialog
        title={t('draft.deleteTitle')}
        message={t('draft.deleteMessage').replace('{0}', activeDraft.name)}
        onCancel={close}
        onConfirm={() => {
          close()
          void useProjectStore.getState().deleteDraft(activeDraft.id)
        }}
      />
    )
  }

  if (dialog === 'renameProject') {
    return (
      <InputDialog
        title={t('command.renameProject')}
        placeholder={projectName ?? ''}
        onCancel={close}
        onSubmit={(name) => {
          close()
          void (async () => {
            const state = await rpc.request<ProjectStateDto>('project/rename', [name])
            useProjectStore.getState().applyState(state)
          })()
        }}
      />
    )
  }

  if (dialog === 'book') {
    return (
      <InputDialog
        title={t('book.addBookTitle')}
        onCancel={close}
        onSubmit={(name) => {
          close()
          void useProjectStore.getState().createBook(name)
        }}
      />
    )
  }

  if (dialog === 'draft') {
    return (
      <InputDialog
        title={t('draft.newTitle')}
        onCancel={close}
        onSubmit={(name) => {
          close()
          void useProjectStore.getState().createDraft(name)
        }}
      />
    )
  }

  if (dialog === 'paneLayouts') return <PaneLayoutsDialog onClose={close} />

  if (dialog === 'createProject') return <CreateProjectDialog onClose={close} />

  if (dialog === 'importManuscript') return <ImportManuscriptDialog onClose={close} />

  if (dialog === 'importPlugin') {
    return (
      <ImportPluginDialog
        onClose={close}
        onImported={(projectPath) => {
          close()
          void useProjectStore.getState().openProject(projectPath)
        }}
      />
    )
  }

  // Named rather than fallen through to: a `ShellDialog` added without a branch
  // here should render nothing and fail typecheck, not quietly raise whichever
  // dialog happened to be last.
  return exhausted(dialog)
}

/** Nothing, and a compile error if a dialog has been added without a branch. */
function exhausted(dialog: never): null {
  void dialog
  return null
}
