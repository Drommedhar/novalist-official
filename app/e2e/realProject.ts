import { existsSync } from 'node:fs'
import { join } from 'node:path'

/**
 * The real book these specs run against.
 *
 * Eighteen specs need a project with actual chapters, scenes and Codex entries
 * in it - a fixture built in the test would not exercise what they are for.
 * Each one used to carry the same hardcoded path to one maintainer's macOS
 * home, so on every other machine `existsSync` came back false and the spec
 * skipped. It skipped quietly: a run reporting "32 passed, 21 skipped" reads
 * as green, and twenty of those skips were UI tests nobody had run in a while.
 *
 * So the path is resolved from candidates instead, and the environment
 * variable still wins for anyone whose copy lives somewhere else. CI has none
 * of these paths and is meant not to: the specs skip there exactly as before,
 * because a checkout has no book in it.
 */
const CANDIDATES = [
  process.env.NOVALIST_REAL_PROJECT,
  join(process.cwd(), '..', '..', 'The-Silent-Shadows'),
  'D:/git/The-Silent-Shadows',
  '/Users/dominikgoblirsch/GIT/The-Silent-Shadows'
]

/** First candidate that is a real Novalist project; the env value otherwise,
 *  so a skip message names something recognisable. */
export const REAL_PROJECT: string =
  CANDIDATES.find((path): path is string => !!path && existsSync(join(path, '.novalist'))) ??
  CANDIDATES[0] ??
  'The-Silent-Shadows'

/** Whether the specs that need a real book can run here. */
export const hasRealProject = (): boolean => existsSync(join(REAL_PROJECT, '.novalist'))
