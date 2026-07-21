import { app } from 'electron'
import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'

/**
 * Security-scoped bookmarks for the Mac App Store (sandboxed) build.
 *
 * Under the App Sandbox, a folder the user picked in a native open panel is only
 * accessible for that session. Reopening a project from a stored path string on a
 * later launch (clicking a recent-project card) has no fresh grant and would be
 * denied. macOS solves this with security-scoped bookmarks: capture an opaque
 * bookmark when the folder is first picked, persist it, then resolve + "start
 * accessing" it before touching the path again.
 *
 * EVERYTHING here is a no-op on any non-App-Store build: `process.mas` is only
 * true for the `mas` target, so Windows, Linux, and the Developer ID DMG all take
 * the trivial paths (saveBookmark does nothing, beginAccess returns true). That
 * keeps the sandbox machinery from ever affecting the direct-download builds.
 */
const isMas = (process as NodeJS.Process & { mas?: boolean }).mas === true

type BookmarkMap = Record<string, string>

// path -> stop function returned by startAccessingSecurityScopedResource.
const activeStops = new Map<string, () => void>()

function storePath(): string {
  return join(app.getPath('userData'), 'mac-bookmarks.json')
}

function load(): BookmarkMap {
  try {
    const p = storePath()
    if (!existsSync(p)) return {}
    return JSON.parse(readFileSync(p, 'utf8')) as BookmarkMap
  } catch {
    // A corrupt/unreadable store must never block the app — start fresh.
    return {}
  }
}

function save(map: BookmarkMap): void {
  writeFileSync(storePath(), JSON.stringify(map), 'utf8')
}

/**
 * Persist a security-scoped bookmark captured from a native picker, keyed by the
 * picked path. No-op off MAS or when the picker returned no bookmark.
 */
export function saveBookmark(path: string, bookmark: string | undefined): void {
  if (!isMas || !bookmark) return
  const map = load()
  map[path] = bookmark
  save(map)
}

/**
 * Begin sandbox access to a previously-picked path via its stored bookmark.
 * Returns true when access is available — always true off MAS, and on MAS when a
 * bookmark resolves. Returns false only on MAS when there is no usable bookmark,
 * so the caller can fall back to re-prompting the user for the folder.
 */
export function beginAccess(path: string): boolean {
  if (!isMas) return true
  if (activeStops.has(path)) return true
  const bookmark = load()[path]
  if (!bookmark) return false
  try {
    // Electron types the stop handle as the broad `Function`; narrow it.
    const stop = app.startAccessingSecurityScopedResource(bookmark) as () => void
    activeStops.set(path, stop)
    return true
  } catch {
    return false
  }
}

/** Release a scoped-resource access started by beginAccess. No-op if inactive. */
export function endAccess(path: string): void {
  const stop = activeStops.get(path)
  if (!stop) return
  stop()
  activeStops.delete(path)
}
