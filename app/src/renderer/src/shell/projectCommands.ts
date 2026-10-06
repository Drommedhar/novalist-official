// aislop-ignore-file code-quality/duplicate-block -- Project command registry entries share a schema but retain distinct IDs, availability predicates, and actions.
import type { CommandDef } from './commandTypes'
import { shell, project, projectOpen } from './commandContext'

export const PROJECT_COMMANDS: CommandDef[] = [
  /* ── Project: the main toolbar ──────────────────────────────────────── */
  {
    id: 'project.newChapter',
    labelKey: 'command.newChapter',
    categoryKey: 'hotkeys.category.scenes',
    scope: 'project',
    available: projectOpen,
    run: () => shell().openDialog('chapter')
  },
  {
    id: 'project.newScene',
    labelKey: 'command.newScene',
    categoryKey: 'hotkeys.category.scenes',
    scope: 'project',
    available: () => projectOpen() && project().chapters.length > 0,
    run: () => shell().openDialog('scene')
  },
  {
    id: 'project.findReplace',
    labelKey: 'findReplace.title',
    categoryKey: 'hotkeys.category.project',
    scope: 'project',
    defaultGesture: 'Ctrl+Shift+F',
    available: projectOpen,
    run: () => shell().setFindReplaceOpen(true)
  },
  {
    id: 'project.cleanup',
    labelKey: 'cleanup.title',
    categoryKey: 'hotkeys.category.project',
    scope: 'project',
    // No default gesture: this rewrites the prose in every scene it touches,
    // and a pass that big should be reached on purpose rather than by a
    // mistyped chord.
    available: projectOpen,
    run: () => shell().setCleanupOpen(true)
  },
  {
    id: 'project.newBook',
    labelKey: 'book.addBookTitle',
    categoryKey: 'hotkeys.category.project',
    scope: 'project',
    available: projectOpen,
    run: () => shell().openDialog('book')
  },
  {
    id: 'project.renameBook',
    labelKey: 'book.renameBookTitle',
    categoryKey: 'hotkeys.category.project',
    scope: 'project',
    available: projectOpen,
    run: () => shell().openDialog('renameBook')
  },
  {
    id: 'project.newDraft',
    labelKey: 'draft.newTitle',
    categoryKey: 'hotkeys.category.project',
    scope: 'project',
    available: projectOpen,
    run: () => shell().openDialog('draft')
  },
  {
    id: 'project.compareDrafts',
    labelKey: 'draftCompare.title',
    categoryKey: 'hotkeys.category.project',
    scope: 'project',
    available: () => projectOpen() && project().drafts.length > 1,
    run: () => shell().openDialog('draftCompare')
  },
  {
    id: 'project.deleteDraft',
    labelKey: 'draft.deleteTitle',
    categoryKey: 'hotkeys.category.project',
    scope: 'project',
    available: () => projectOpen() && project().drafts.length > 1,
    run: () => shell().openDialog('deleteDraft')
  },
  {
    id: 'project.rename',
    labelKey: 'command.renameProject',
    categoryKey: 'hotkeys.category.project',
    scope: 'project',
    available: projectOpen,
    run: () => shell().openDialog('renameProject')
  },
  {
    id: 'project.quickCapture',
    labelKey: 'capture.quickTitle',
    categoryKey: 'hotkeys.category.project',
    scope: 'project',
    defaultGesture: 'Ctrl+Shift+K',
    home: 'menuBar',
    // Its whole point is being reachable with nothing open and nothing in
    // front of you, including before a project has been chosen.
    homeNote: 'Works with no project open, so it cannot live on the project bar.',
    run: () => shell().setQuickCaptureOpen(true)
  }
]
