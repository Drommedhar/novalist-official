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
