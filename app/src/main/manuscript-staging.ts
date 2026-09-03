import { app } from 'electron'
import { cp, copyFile, lstat, mkdir, mkdtemp, readdir, rm, stat } from 'node:fs/promises'
import { rmSync } from 'node:fs'
import { basename, dirname, extname, isAbsolute, join, normalize, relative, sep } from 'node:path'

/**
 * Sources selected through a Mac App Store open panel are readable by Electron,
 * but its later PowerBox grant does not cross into the .NET child that performs
 * manuscript import. Copy the selected source into the app container, which
 * both processes can read, and retain only the path needed for this dialog.
 */
const stagedSources = new Map<string, string>()
let stagingReady: Promise<void> | null = null
let quitCleanupRegistered = false

type ProjectValidationCode =
  | 'NOVALIST_MANIFEST_NOT_FOUND'
  | 'NOVALIST_MANIFEST_AMBIGUOUS'
  | 'NOVALIST_INVALID_MANIFEST'

function projectValidationError(code: ProjectValidationCode, message: string): Error {
  const error = new Error(message) as Error & { code: ProjectValidationCode }
  error.code = code
  return error
}

function stagingBase(): string {
  return join(app.getPath('userData'), 'manuscript-import-staging')
}

function isDirectChildOfStagingBase(path: string): boolean {
  return normalize(dirname(path)) === normalize(stagingBase())
}

function isScrivenerManifest(name: string): boolean {
  return extname(name).toLowerCase() === '.scrivx'
}

/** Copies only what the importer reads, never an arbitrary selected directory. */
async function copyScrivenerProject(
  projectRoot: string,
  stagedProject: string,
  selectedManifest?: string
): Promise<string> {
  const entries = await readdir(projectRoot, { withFileTypes: true })
  let manifestName: string

  if (selectedManifest) {
    const selectedRelative = relative(projectRoot, selectedManifest)
    if (
      selectedRelative.length === 0 ||
      selectedRelative === '..' ||
      selectedRelative.startsWith(`..${sep}`) ||
      isAbsolute(selectedRelative) ||
      dirname(selectedRelative) !== '.' ||
      !isScrivenerManifest(selectedRelative) ||
      !(await lstat(selectedManifest)).isFile()
    ) {
      throw projectValidationError(
        'NOVALIST_INVALID_MANIFEST',
        'Selected manifest is outside its granted project folder.'
      )
    }
    manifestName = basename(selectedManifest)
  } else {
    const manifests = entries.filter(
      (entry) => entry.isFile() && isScrivenerManifest(entry.name)
    )
    if (manifests.length !== 1) {
      throw projectValidationError(
        manifests.length === 0
          ? 'NOVALIST_MANIFEST_NOT_FOUND'
          : 'NOVALIST_MANIFEST_AMBIGUOUS',
        manifests.length === 0
          ? 'Selected directory has no Scrivener manifest.'
          : 'Selected directory has more than one Scrivener manifest.'
      )
    }
    manifestName = manifests[0].name
  }

  await mkdir(stagedProject)
  await copyFile(
    selectedManifest ?? join(projectRoot, manifestName),
    join(stagedProject, manifestName)
  )

  // All manuscript prose and file-backed research is under Files. Icons,
  // snapshots and application settings are not parsed and must not be copied.
  const filesEntry =
    entries.find((entry) => entry.name === 'Files') ??
    entries.find((entry) => entry.name.toLowerCase() === 'files')
  if (filesEntry && (await stat(join(projectRoot, filesEntry.name))).isDirectory()) {
    await cp(join(projectRoot, filesEntry.name), join(stagedProject, filesEntry.name), {
      recursive: true,
      preserveTimestamps: true,
      // A research symlink may point anywhere on disk. The importer does not
      // follow it while staging or duplicate data outside the chosen project.
      filter: async (source) => !(await lstat(source)).isSymbolicLink()
    })
  }

  return selectedManifest ? join(stagedProject, manifestName) : stagedProject
}

/** Clears copies left by a crash and prepares an empty per-profile staging area. */
export function initializeManuscriptStaging(): Promise<void> {
  if (!stagingReady) {
    const base = stagingBase()
    stagingReady = rm(base, { recursive: true, force: true }).then(() =>
      mkdir(base, { recursive: true }).then(() => undefined)
    )
  }

  if (!quitCleanupRegistered) {
    quitCleanupRegistered = true
    app.once('will-quit', () => {
      const base = stagingBase()
      if (normalize(dirname(base)) !== normalize(app.getPath('userData'))) return
      try {
        rmSync(base, { recursive: true, force: true })
      } catch (error) {
        const type = error instanceof Error ? error.name : 'UnknownError'
        console.error(`[manuscript-import] could not clear staging on exit (${type}).`)
      }
    })
  }

  return stagingReady
}

/**
 * Copies either one manuscript file or the parseable portion of a Scrivener
 * project into the app container. When projectRoot is supplied, selectedPath
 * remains the exact manifest returned to the backend while its payload is
 * copied too.
 */
export async function stageManuscriptSource(
  selectedPath: string,
  projectRoot?: string
): Promise<string> {
  await initializeManuscriptStaging()

  const source = projectRoot ?? selectedPath
  const info = await stat(source)
  const stageRoot = await mkdtemp(join(stagingBase(), 'source-'))
  const stagedSource = join(stageRoot, basename(source) || 'source')

  try {
    let stagedSelection: string
    if (info.isDirectory()) {
      stagedSelection = await copyScrivenerProject(
        source,
        stagedSource,
        projectRoot ? selectedPath : undefined
      )
    } else {
      await copyFile(source, stagedSource)
      stagedSelection = stagedSource
    }

    stagedSources.set(stagedSelection, stageRoot)
    return stagedSelection
  } catch (error) {
    if (isDirectChildOfStagingBase(stageRoot)) {
      await rm(stageRoot, { recursive: true, force: true })
    }
    throw error
  }
}

/** Deletes only a staging root created for the supplied returned path. */
export async function releaseStagedManuscript(path: string): Promise<void> {
  const stageRoot = stagedSources.get(path)
  if (!stageRoot) return
  if (!isDirectChildOfStagingBase(stageRoot)) return
  await rm(stageRoot, { recursive: true, force: true })
  stagedSources.delete(path)
}
