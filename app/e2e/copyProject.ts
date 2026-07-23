import { cpSync } from 'node:fs'
import { sep } from 'node:path'

/** Directories never worth copying into a throwaway test project. */
const EXCLUDED = new Set(['.git', '.obsidian', '.claude'])

/**
 * Copies a real project into a scratch directory for a test to mutate.
 *
 * Uses Node's own recursive copy rather than shelling out to `rsync`, which does
 * not exist on Windows — the tests that needed it simply could not run there.
 */
export function copyProject(source: string, destination: string): void {
  cpSync(source, destination, {
    recursive: true,
    filter: (src) => !src.split(sep).some((segment) => EXCLUDED.has(segment))
  })
}
