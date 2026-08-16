import { useEditorBridge } from '../stores/editorBridgeStore'
import { useProjectStore } from '../stores/projectStore'
import { useSettingsStore } from '../stores/settingsStore'
import { useShellStore, type MainView } from '../stores/shellStore'
import { useUiScaleStore } from '../stores/uiScaleStore'
import type { EditorWindow } from '../views/editor/editorBridge'
import { popOut } from './PaneHeader'
import { printCurrentView } from './printView'

/**
 * Every command Novalist has, and the one surface each of them lives in.
 *
 * The interface grew a command at a time, and each one picked a home on the
 * day it was written: Comment ended up in the selection toolbar, the editor
 * toolbar and the context menu, the panel toggles in both the main toolbar and
 * the menu bar, and the command palette indexed only the two dozen actions
 * that happened to carry a hotkey. Nothing connected the kind of a command to
 * the kind of container it belonged in, so there was nothing to learn.
 *
 * The placement law this registry enforces:
 *
 *   A command's scope decides its home, it has exactly one persistent home,
 *   and it is also in the command palette.
 *
 * Scope decides the home ({@link DEFAULT_HOME}); a command that needs a
 * different one has to say why in `homeNote`, which is the only way a
 * deviation gets written down rather than merely happening.
 *
 * `tools/placement-doctor.py` reads this file and the surfaces that render
 * from it, and fails the build when a command reaches two persistent
 * containers or none.
 */

/** What a command acts on. */
export type CommandScope =
  /** The current text selection. */
  | 'selection'
  /** The object under the caret or the pointer - an entity, a word, a link. */
  | 'caret'
  /** The paragraph the caret is in. */
  | 'paragraph'
  /** The open view as a whole. */
  | 'view'
  /** The open project. */
  | 'project'
  /** The application. */
  | 'application'

/** The persistent surfaces a command can live in. */
export type CommandContainer =
  | 'selectionToolbar'
  | 'contextMenu'
  /** The open view's own command bar - the editor toolbar, in the writing view. */
  | 'viewBar'
  /** The main toolbar, which is the open project's command bar. */
  | 'projectBar'
  | 'menuBar'

/**
 * Where each scope puts a command.
 *
 * The law's table, as code. Two rows deserve their reasoning written down,
 * because the plan's prose is shorter than the distinction it draws:
 *
 * - `caret` and `paragraph` are both "under the caret", and the plan gave them
 *   one row. They are not one thing. A command acting on an *object* - peek at
 *   this entity, split the scene here, insert an image - belongs to the menu
 *   you raise on that object. A command acting on the *paragraph* - its style,
 *   its list, its alignment - is structural, applies wherever the caret is,
 *   and belongs on the bar that is always in front of the writer. Putting
 *   alignment behind a right-click would be a worse app that satisfied a
 *   shorter rule.
 * - `project` and `application` were also one row. Novalist's main toolbar is
 *   the project's command bar in exactly the sense the editor toolbar is the
 *   writing view's, and the placement audit's verdict keeps it that way.
 */
export const DEFAULT_HOME: Record<CommandScope, CommandContainer> = {
  selection: 'selectionToolbar',
  caret: 'contextMenu',
  paragraph: 'viewBar',
  view: 'viewBar',
  project: 'projectBar',
  application: 'menuBar'
}

/**
 * A command's declared placement. Taking the default costs nothing; taking
 * anything else costs a sentence, which is the point - the deviations are the
 * part worth reviewing.
 */
type Placement =
  | { scope: CommandScope }
  | { scope: CommandScope; home: CommandContainer; homeNote: string }

export type CommandDef = {
  /** Stable id. Also what the palette and the placement doctor match on. */
  id: string
  /** Localization key for the command's name, in the palette and its container. */
  labelKey: string
  /** Localization key for the settings category its gesture groups under. */
  categoryKey: string
  /**
   * Factory default gesture, in the Avalonia KeyGesture grammar. A hotkey is a
   * property of a command rather than the thing that makes one exist, so most
   * commands have none and every one of them can be given one in Settings.
   */
  defaultGesture?: string
  /**
   * Whether the command can do anything right now. The palette hides the ones
   * that cannot, so a line in it is never a line that fails when clicked.
   * Absent means always.
   */
  available?(): boolean
  run(): void
} & Placement

const shell = (): ReturnType<typeof useShellStore.getState> => useShellStore.getState()
const project = (): ReturnType<typeof useProjectStore.getState> => useProjectStore.getState()

/** The editor showing the scene the writer is in, or null. */
function liveEditor(): EditorWindow | null {
  return useEditorBridge.getState().editor
}

/** Wraps an editor call so a command is a no-op rather than a throw with none open. */
function inEditor(command: (editor: EditorWindow) => void): () => void {
  return () => {
    const live = liveEditor()
    if (live) command(live)
  }
}

const editorOpen = (): boolean => liveEditor() !== null
const hasSelection = (): boolean =>
  liveEditor() !== null && useEditorBridge.getState().hasSelection
const projectOpen = (): boolean => project().isLoaded
const sceneOpen = (): boolean => {
  const state = project()
  return state.openChapterGuid !== null && state.openSceneId !== null
}

/** Applies one editor preference at whichever scope the writer is editing. */
function updateEditorSetting(patch: Record<string, unknown>): void {
  const view = useSettingsStore.getState().view
  const scope = view?.overriddenSections?.editor ? 'project' : 'global'
  void useSettingsStore.getState().update(scope, patch)
}

/** Reads one boolean editor preference, defaulting to off. */
function editorFlag(key: string): boolean {
  const effective = useSettingsStore.getState().view?.effective as
    | Record<string, unknown>
    | undefined
  return effective?.[key] === true
}

/** Flips one boolean editor preference. */
function toggleEditorSetting(key: string): () => void {
  return () => updateEditorSetting({ [key]: !editorFlag(key) })
}

/**
 * Every view, in the order the modes hold them. Navigation is
 * application-scoped: which destination is in front of the writer is a fact
 * about the window rather than about the book.
 */
const NAV_GESTURES: Partial<Record<MainView, string>> = {
  write: 'Ctrl+D1',
  dashboard: 'Ctrl+D2',
  timeline: 'Ctrl+D3',
  codex: 'Ctrl+D4',
  manuscript: 'Ctrl+D5',
  calendar: 'Ctrl+D6',
  relationships: 'Ctrl+D7',
  plotGrid: 'Ctrl+D8',
  research: 'Ctrl+D9'
}

const NAV_VIEWS: MainView[] = [
  'write',
  'dashboard',
  'manuscript',
  'timeline',
  'plotGrid',
  'calendar',
  'relationships',
  'dialogue',
  'canvas',
  'series',
  'codex',
  'wiki',
  'maps',
  'languages',
  'research',
  'gallery',
  'expose',
  'export',
  'style',
  'git',
  'extensions',
  'settings'
]

/** Views that mean something with no project open. */
const VIEWS_WITHOUT_PROJECT = new Set<MainView>(['settings', 'extensions'])

const NAV_COMMANDS: CommandDef[] = NAV_VIEWS.map((view) => ({
  id: `nav.${view}`,
  labelKey: `shell.view.${view}`,
  categoryKey: 'hotkeys.category.navigation',
  scope: 'application' as const,
  ...(NAV_GESTURES[view] ? { defaultGesture: NAV_GESTURES[view] } : {}),
  available: () => projectOpen() || VIEWS_WITHOUT_PROJECT.has(view),
  run: () => (view === 'settings' ? shell().openSettings() : shell().setMainView(view))
}))

/** The named block styles a scene can carry. Body is the absence of a style. */
const PARAGRAPH_STYLES = ['', 'heading', 'subheading', 'blockquote', 'poetry'] as const

const PARAGRAPH_STYLE_COMMANDS: CommandDef[] = PARAGRAPH_STYLES.map((style) => ({
  id: `paragraph.style.${style || 'body'}`,
  labelKey: `blockStyle.${style || 'body'}`,
  categoryKey: 'hotkeys.category.editor',
  scope: 'paragraph' as const,
  available: editorOpen,
  run: inEditor((editor) => editor.setParagraphStyle(style))
}))

/**
 * The registry.
 *
 * Grouped by scope so the law is legible down the page rather than only
 * enforceable by a script.
 */
export const COMMANDS: CommandDef[] = [
  /* ── Selection: the floating toolbar over the prose ─────────────────── */
  {
    id: 'text.bold',
    labelKey: 'blockStyle.bold',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    // The gesture every writing program has had for thirty years. It was on
    // the binder instead, so on Windows and Linux there was no way to bold a
    // word from the keyboard at all - the binder has moved rather than bold.
    defaultGesture: 'Ctrl+B',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleBold())
  },
  {
    id: 'text.italic',
    labelKey: 'blockStyle.italic',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    // The editor forwards Ctrl+I and Ctrl+U to the host and suppresses the
    // native behaviour on the way, so with nothing bound here they did nothing
    // at all - worse than either half alone. (Cmd+I and Cmd+U stay native on
    // macOS, which editor.html deliberately protects.)
    defaultGesture: 'Ctrl+I',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleItalic())
  },
  {
    id: 'text.underline',
    labelKey: 'blockStyle.underline',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    defaultGesture: 'Ctrl+U',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleUnderline())
  },
  {
    id: 'text.strikethrough',
    labelKey: 'blockStyle.strikethrough',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleStrikethrough())
  },
  {
    id: 'text.highlight',
    labelKey: 'blockStyle.highlight',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleHighlight())
  },
  {
    id: 'text.link',
    labelKey: 'blockStyle.link',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    available: hasSelection,
    // The address is asked for by the frame around the editor, which is where
    // the prompt lives; the bridge only carries the answer back.
    run: () => useEditorBridge.getState().requestLink?.()
  },
  {
    id: 'text.comment',
    labelKey: 'blockStyle.comment',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    defaultGesture: 'Ctrl+Shift+M',
    available: hasSelection,
    run: inEditor((editor) => editor.addCommentToSelection(crypto.randomUUID()))
  },
  {
    id: 'text.footnote',
    labelKey: 'blockStyle.footnote',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    available: editorOpen,
    run: inEditor((editor) => editor.insertFootnoteAtSelection(crypto.randomUUID()))
  },
  {
    id: 'text.cutToDarlings',
    labelKey: 'editor.contextMenu.cutToDarlings',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    home: 'contextMenu',
    // Reads as one gesture with the passage it acts on, and the floating
    // toolbar is eight buttons of formatting - a command that removes prose
    // does not belong beside Bold.
    homeNote: 'Destructive, and read as part of the Scene group it sits in.',
    available: hasSelection,
    run: inEditor((editor) => editor.runContextAction('cutToDarlings'))
  },
  {
    id: 'text.createEntity',
    labelKey: 'editor.contextMenu.createEntity',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    home: 'contextMenu',
    homeNote: 'Belongs with the other Codex actions on the passage, not with formatting.',
    available: hasSelection,
    run: inEditor((editor) => editor.runContextAction('createEntityFromSelection'))
  },
  {
    id: 'text.appendToEntity',
    labelKey: 'editor.contextMenu.appendToEntity',
    categoryKey: 'hotkeys.category.editor',
    scope: 'selection',
    home: 'contextMenu',
    homeNote: 'Belongs with the other Codex actions on the passage, not with formatting.',
    available: hasSelection,
    run: inEditor((editor) => editor.runContextAction('appendToEntitySection'))
  },

  /* ── Caret: the editor's context menu ───────────────────────────────── */
  {
    id: 'caret.peekEntity',
    labelKey: 'blockStyle.peekEntity',
    categoryKey: 'hotkeys.category.editor',
    scope: 'caret',
    defaultGesture: 'Ctrl+Shift+E',
    available: () => editorOpen() && useEditorBridge.getState().entityAtCaret,
    run: inEditor((editor) => editor.peekEntityAtCaret())
  },
  {
    id: 'caret.splitScene',
    labelKey: 'editor.contextMenu.splitScene',
    categoryKey: 'hotkeys.category.scenes',
    scope: 'caret',
    available: editorOpen,
    run: inEditor((editor) => editor.runContextAction('splitAtCaret'))
  },
  {
    id: 'caret.insertImage',
    labelKey: 'editor.contextMenu.insertImage',
    categoryKey: 'hotkeys.category.editor',
    scope: 'caret',
    available: editorOpen,
    run: inEditor((editor) => editor.runContextAction('insertImage'))
  },

  /* ── Paragraph: the writing view's command bar ──────────────────────── */
  ...PARAGRAPH_STYLE_COMMANDS,
  {
    id: 'paragraph.bulletList',
    labelKey: 'blockStyle.bulletList',
    categoryKey: 'hotkeys.category.editor',
    scope: 'paragraph',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleBulletList())
  },
  {
    id: 'paragraph.numberList',
    labelKey: 'blockStyle.numberList',
    categoryKey: 'hotkeys.category.editor',
    scope: 'paragraph',
    available: editorOpen,
    run: inEditor((editor) => editor.toggleNumberList())
  },
  {
    id: 'paragraph.alignLeft',
    labelKey: 'blockStyle.left',
    categoryKey: 'hotkeys.category.editor',
    scope: 'paragraph',
    available: editorOpen,
    run: inEditor((editor) => editor.alignLeft())
  },
  {
    id: 'paragraph.alignCenter',
    labelKey: 'blockStyle.center',
    categoryKey: 'hotkeys.category.editor',
    scope: 'paragraph',
    available: editorOpen,
    run: inEditor((editor) => editor.alignCenter())
  },
  {
    id: 'paragraph.alignRight',
    labelKey: 'blockStyle.right',
    categoryKey: 'hotkeys.category.editor',
    scope: 'paragraph',
    available: editorOpen,
    run: inEditor((editor) => editor.alignRight())
  },
  {
    id: 'paragraph.alignJustify',
    labelKey: 'blockStyle.justify',
    categoryKey: 'hotkeys.category.editor',
    scope: 'paragraph',
    available: editorOpen,
    run: inEditor((editor) => editor.alignJustify())
  },

  /* ── The writing view itself ────────────────────────────────────────── */
  {
    id: 'write.snapshots',
    labelKey: 'shell.snapshots',
    categoryKey: 'hotkeys.category.scenes',
    scope: 'view',
    available: sceneOpen,
    run: () => shell().openDialog('snapshots')
  },
  {
    id: 'write.suggestionMode',
    labelKey: 'suggestions.mode',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: () => shell().toggleSuggestionMode()
  },
  {
    id: 'write.readAloud',
    labelKey: 'blockStyle.readAloud',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: () => useEditorBridge.getState().toggleReadAloud?.()
  },
  {
    id: 'write.readability',
    labelKey: 'blockStyle.readability',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: toggleEditorSetting('readabilityHighlighting')
  },
  {
    id: 'write.composeDimming',
    labelKey: 'blockStyle.composeDimming',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: toggleEditorSetting('composeDimming')
  },
  {
    id: 'write.typewriterScrolling',
    labelKey: 'blockStyle.typewriterScrolling',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: toggleEditorSetting('typewriterScrollEnabled')
  },
  {
    id: 'write.pageView',
    labelKey: 'blockStyle.pageView',
    categoryKey: 'hotkeys.category.editor',
    scope: 'view',
    available: editorOpen,
    run: toggleEditorSetting('pageViewEnabled')
  },

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
  },

  /* ── Application: the menu bar ──────────────────────────────────────── */
  ...NAV_COMMANDS,
  {
    id: 'app.commandPalette',
    labelKey: 'commandPalette.placeholder',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    defaultGesture: 'Ctrl+Shift+P',
    run: () => shell().setCommandPaletteOpen(true)
  },
  {
    id: 'app.quickOpen',
    labelKey: 'quickOpen.placeholder',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    defaultGesture: 'Ctrl+P',
    available: projectOpen,
    run: () => shell().setQuickOpenOpen(true)
  },
  {
    // Everything from here down shapes the workspace, and with no project open
    // there is no workspace on screen to shape - so these were menu items that
    // looked live and did nothing when pressed.
    id: 'app.print',
    labelKey: 'print.title',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    // Ctrl+P is Quick Open here and has been since before there was anything
    // to print, so moving it would cost more than it is worth.
    defaultGesture: 'Ctrl+Alt+P',
    available: projectOpen,
    run: printCurrentView
  },
  {
    id: 'app.toggleBinder',
    labelKey: 'shell.toggleBinder',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    // Ctrl+B belongs to bold. The two panels keep the same shape of gesture as
    // each other so they stay one thing to remember rather than two.
    defaultGesture: 'Ctrl+Alt+B',
    available: projectOpen,
    run: () => shell().toggleBinder()
  },
  {
    id: 'app.toggleInspector',
    labelKey: 'shell.toggleInspector',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Alt+I',
    available: projectOpen,
    run: () => shell().toggleInspector()
  },
  {
    id: 'app.toggleModePanel',
    labelKey: 'modes.togglePanel',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    available: projectOpen,
    run: () => shell().toggleModePanelDocked()
  },
  {
    id: 'app.toggleSceneNotes',
    labelKey: 'shell.toggleSceneNotes',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Shift+N',
    available: projectOpen,
    run: () => shell().toggleNotesDock()
  },
  {
    id: 'app.focusMode',
    labelKey: 'menu.focusMode',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Alt+F',
    available: projectOpen,
    run: () => shell().toggleFocusMode()
  },
  {
    id: 'app.splitRight',
    labelKey: 'panes.splitRight',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Alt+ArrowRight',
    available: projectOpen,
    run: () => shell().splitActivePane('row')
  },
  {
    id: 'app.splitDown',
    labelKey: 'panes.splitDown',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Alt+ArrowDown',
    available: projectOpen,
    run: () => shell().splitActivePane('column')
  },
  {
    id: 'app.closePane',
    labelKey: 'panes.close',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Alt+W',
    available: projectOpen,
    run: () => shell().closeActivePane()
  },
  {
    id: 'app.resetPanes',
    labelKey: 'panes.defaultLayout',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    available: projectOpen,
    run: () => shell().resetPanes()
  },
  {
    id: 'app.popOut',
    labelKey: 'panes.popOut',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    available: projectOpen,
    run: () => void popOut(shell().mainView)
  },
  {
    id: 'app.paneLayouts',
    labelKey: 'panes.layouts',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    available: projectOpen,
    run: () => shell().openDialog('paneLayouts')
  },
  {
    id: 'app.layouts',
    labelKey: 'layouts.title',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Alt+L',
    available: projectOpen,
    run: () => shell().setLayoutsOpen(true)
  },
  {
    // These three were accelerators the main process registered itself. The
    // menu bar shows gestures now but does not bind them - the renderer is the
    // only dispatcher - so they have to be real bindings here or the keys stop
    // working, which is exactly what happened.
    id: 'app.uiScaleIncrease',
    labelKey: 'command.uiScaleIncrease',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Plus',
    run: () => useUiScaleStore.getState().increase()
  },
  {
    id: 'app.uiScaleDecrease',
    labelKey: 'command.uiScaleDecrease',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+Minus',
    run: () => useUiScaleStore.getState().decrease()
  },
  {
    id: 'app.uiScaleReset',
    labelKey: 'command.uiScaleReset',
    categoryKey: 'hotkeys.category.panels',
    scope: 'application',
    defaultGesture: 'Ctrl+D0',
    run: () => useUiScaleStore.getState().reset()
  },
  {
    id: 'app.tour',
    labelKey: 'tour.title',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    defaultGesture: 'Ctrl+Alt+T',
    available: projectOpen,
    run: () => shell().setTourOpen(true)
  },
  {
    id: 'app.manual',
    labelKey: 'command.manual',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    // F1 was an accelerator the Help menu registered. The menu bar no longer
    // binds anything, so the manual's only keyboard route lives here.
    defaultGesture: 'F1',
    run: () => shell().setHelpOpen(true)
  },
  {
    id: 'app.newProject',
    labelKey: 'welcome.newProject',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    run: () => shell().openDialog('createProject')
  },
  {
    id: 'app.openProject',
    labelKey: 'welcome.browseFolder',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    run: () => void project().pickAndOpenProject()
  },
  {
    id: 'app.closeProject',
    labelKey: 'command.closeProject',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    available: projectOpen,
    run: () => void project().closeProject()
  },
  {
    id: 'app.importProject',
    labelKey: 'welcome.importPlugin',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    run: () => shell().openDialog('importPlugin')
  },
  {
    id: 'app.importManuscript',
    labelKey: 'manuscriptImport.action',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    available: projectOpen,
    run: () => shell().openDialog('importManuscript')
  },
  {
    id: 'app.about',
    labelKey: 'command.about',
    categoryKey: 'hotkeys.category.general',
    scope: 'application',
    // Reachable with nothing open: the version, the licences and the changelog
    // are facts about the installation rather than about a book.
    run: () => shell().setMainView('about')
  }
]

/** Where this command actually lives. */
export function homeOf(command: CommandDef): CommandContainer {
  return 'home' in command ? command.home : DEFAULT_HOME[command.scope]
}

/** Every command whose one persistent home is this container, in registry order. */
export function commandsIn(container: CommandContainer): CommandDef[] {
  return COMMANDS.filter((command) => homeOf(command) === container)
}

const BY_ID = new Map(COMMANDS.map((command) => [command.id, command]))

export function commandById(id: string): CommandDef | undefined {
  return BY_ID.get(id)
}

/** Runs a command by id. Used by the surfaces that render from the registry. */
export function runCommand(id: string): void {
  BY_ID.get(id)?.run()
}
