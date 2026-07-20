import { app, shell, type BrowserWindow } from 'electron'
import { createWriteStream, existsSync, mkdirSync, statSync, unlinkSync } from 'node:fs'
import { join } from 'node:path'
import { pipeline } from 'node:stream/promises'
import { Readable } from 'node:stream'

/**
 * App self-update ported from the desktop Novalist.Core UpdateService: query the
 * GitHub "latest release", compare versions, download the platform installer
 * asset, and open it. Signing-independent (unlike electron-updater's Squirrel
 * auto-install), matching the behavior that worked before the Electron rewrite.
 *
 * Repo owner/repo can be changed here if the release location moves.
 */
const RELEASE_OWNER = 'Drommedhar'
const RELEASE_REPO = 'novalist-official'
const RELEASES_API = `https://api.github.com/repos/${RELEASE_OWNER}/${RELEASE_REPO}/releases/latest`

export interface AppUpdateInfo {
  version: string
  tagName: string
  htmlUrl: string
  notes: string
  downloadUrl: string
  assetName: string
  assetSize: number
}

interface GitHubAsset {
  name?: string
  browser_download_url?: string
  size?: number
}
interface GitHubRelease {
  tag_name?: string
  html_url?: string
  body?: string
  assets?: GitHubAsset[]
}

function stripPreRelease(v: string): string {
  const dash = v.indexOf('-')
  return dash >= 0 ? v.slice(0, dash) : v
}

function parts(v: string): number[] {
  return stripPreRelease(v.trim().replace(/^v/i, ''))
    .split('.')
    .map((p) => parseInt(p, 10) || 0)
}

/**
 * The version the update check treats as "currently installed". Normally the
 * packaged app version; setting NOVALIST_FORCE_VERSION lets a developer pretend
 * to be an older (or newer) build to exercise the self-update flow in dev.
 */
function currentAppVersion(): string {
  return process.env.NOVALIST_FORCE_VERSION?.trim() || app.getVersion()
}

/** True when `remote` is a strictly newer semver than `current`. */
export function isNewer(remote: string, current: string): boolean {
  const r = parts(remote)
  const c = parts(current)
  for (let i = 0; i < 3; i++) {
    const a = r[i] ?? 0
    const b = c[i] ?? 0
    if (a > b) return true
    if (a < b) return false
  }
  return false
}

/** Picks the release asset for the running platform/arch, robust to naming. */
function findPlatformAsset(assets: GitHubAsset[]): GitHubAsset | null {
  const named = assets.filter((a) => a.name)
  if (process.platform === 'win32') {
    return named.find((a) => a.name!.toLowerCase().endsWith('.exe')) ?? null
  }
  if (process.platform === 'darwin') {
    const dmgs = named.filter((a) => a.name!.toLowerCase().endsWith('.dmg'))
    const archSuffix = process.arch === 'arm64' ? 'arm64' : 'x64'
    return dmgs.find((a) => a.name!.toLowerCase().includes(archSuffix)) ?? dmgs[0] ?? null
  }
  return named.find((a) => a.name!.toLowerCase().endsWith('.appimage')) ?? null
}

/** Checks GitHub for a newer app release; returns null when up to date / none. */
export async function checkAppUpdate(): Promise<AppUpdateInfo | null> {
  const res = await fetch(RELEASES_API, {
    headers: { Accept: 'application/vnd.github+json', 'User-Agent': 'Novalist' }
  })
  if (!res.ok) throw new Error(`GitHub releases: HTTP ${res.status}`)
  const release = (await res.json()) as GitHubRelease
  if (!release.tag_name) return null

  const remote = release.tag_name.replace(/^v/i, '')
  if (!isNewer(remote, currentAppVersion())) return null

  const asset = release.assets ? findPlatformAsset(release.assets) : null
  if (!asset?.browser_download_url) return null

  return {
    version: remote,
    tagName: release.tag_name,
    htmlUrl: release.html_url ?? '',
    notes: release.body ?? '',
    downloadUrl: asset.browser_download_url,
    assetName: asset.name ?? `novalist-${remote}`,
    assetSize: asset.size ?? 0
  }
}

function downloadDir(): string {
  const dir = join(app.getPath('temp'), 'Novalist', 'Updates')
  mkdirSync(dir, { recursive: true })
  return dir
}

/**
 * Downloads the update asset (reporting 0..1 progress to the window) and opens
 * it so the OS installer/dmg takes over. Returns the downloaded file path.
 */
export async function downloadAndInstall(
  info: AppUpdateInfo,
  win: BrowserWindow
): Promise<string> {
  const filePath = join(downloadDir(), info.assetName)
  if (existsSync(filePath) && info.assetSize > 0 && statSync(filePath).size === info.assetSize) {
    if (!win.isDestroyed()) win.webContents.send('novalist:update-progress', 100)
  } else {
    if (existsSync(filePath)) unlinkSync(filePath)
    const res = await fetch(info.downloadUrl, { headers: { 'User-Agent': 'Novalist' } })
    if (!res.ok || !res.body) throw new Error(`Download failed: HTTP ${res.status}`)
    const total = Number(res.headers.get('content-length')) || info.assetSize
    let received = 0
    const body = Readable.fromWeb(res.body as Parameters<typeof Readable.fromWeb>[0])
    body.on('data', (chunk: Buffer) => {
      received += chunk.length
      if (total > 0 && !win.isDestroyed())
        win.webContents.send('novalist:update-progress', Math.round((received / total) * 100))
    })
    await pipeline(body, createWriteStream(filePath))
    if (!win.isDestroyed()) win.webContents.send('novalist:update-progress', 100)
  }
  // Hand off to the OS: opens the .dmg / runs the .exe / launches the AppImage.
  await shell.openPath(filePath)
  return filePath
}
