/**
 * novalist:// links, so something outside the app can point at a place in it.
 *
 * Novalist registered only its two internal schemes, so nothing else could link
 * to a scene - not a task in a tracker, not a note in another app, not a row in
 * a spreadsheet of what still needs revising. Obsidian gives this away and it
 * is most of what makes a tool linkable at all.
 *
 * Parsing is kept apart from acting on it: the shapes below are what needs
 * checking, and none of it should require an app to be running to reason about.
 */

/** What a link asked for. */
export interface DeepLink {
  /** The project folder to open. Always present on a valid link. */
  project: string
  /** Chapter to open, when the link points at a scene. */
  chapter?: string
  /** Scene to open. Meaningless without a chapter, and dropped without one. */
  scene?: string
}

export const SCHEME = 'novalist'

/**
 * Reads one link, or returns null.
 *
 * Deliberately strict. A link is something another program generated, and a
 * half-understood one that opened the wrong project would be worse than one
 * that did nothing at all.
 */
export function parseDeepLink(raw: string | undefined | null): DeepLink | null {
  if (!raw) return null

  let url: URL
  try {
    url = new URL(raw)
  } catch {
    return null
  }

  if (url.protocol !== `${SCHEME}:`) return null
  // novalist://open?... - the host is the verb. Only one so far, but a link
  // naming a verb this build does not have must not be guessed at.
  if (url.hostname !== 'open') return null

  const project = url.searchParams.get('project')?.trim()
  if (!project) return null

  const chapter = url.searchParams.get('chapter')?.trim() || undefined
  const scene = url.searchParams.get('scene')?.trim() || undefined

  // A scene id means nothing without the chapter that holds it, and opening the
  // project while silently ignoring half the link is the confusing outcome.
  return { project, chapter, scene: chapter ? scene : undefined }
}

/**
 * The first novalist:// argument on a command line, if any.
 *
 * Windows and Linux deliver a link as an argument to a second instance rather
 * than as an event, mixed in with whatever else the launcher passed.
 */
export function deepLinkFromArgv(argv: readonly string[]): DeepLink | null {
  for (const arg of argv) {
    const link = parseDeepLink(arg)
    if (link) return link
  }
  return null
}
