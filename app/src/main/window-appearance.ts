import { BrowserWindow, screen, type BrowserWindowConstructorOptions } from 'electron'
import { join } from 'node:path'
import { existsSync } from 'node:fs'
import { detectMaterial, materialWindowOptions } from './glass'

export const material = detectMaterial(process.platform, process.getSystemVersion())

/** Resolves the app icon: packaged resources first, then the repo dev path. */
export function resolveIconPath(): string | null {
  const candidates = [
    join(process.resourcesPath, 'icon.png'),
    join(__dirname, '..', '..', 'resources', 'icon.png'),
    join(__dirname, '..', '..', 'build', 'icon.png')
  ]
  return candidates.find((p) => existsSync(p)) ?? null
}

/** A restore-down size expressed in Electron's display-independent pixels. */
export function initialWindowGeometry(): {
  width: number
  height: number
  minWidth: number
  minHeight: number
} {
  const workArea = screen.getPrimaryDisplay().workAreaSize
  return {
    width: Math.min(1440, workArea.width),
    height: Math.min(900, workArea.height),
    minWidth: Math.min(760, workArea.width),
    minHeight: Math.min(520, workArea.height)
  }
}

/**
 * Keeps a restored window reachable after a monitor is removed or its display
 * scale changes. Bounds and work areas are both DIPs, so OS DPI remains native.
 */
export function clampWindowToDisplay(win: BrowserWindow): void {
  if (win.isDestroyed() || win.isMaximized() || win.isFullScreen() || win.isMinimized()) return
  const bounds = win.getBounds()
  const area = screen.getDisplayMatching(bounds).workArea
  const width = Math.min(Math.max(bounds.width, Math.min(760, area.width)), area.width)
  const height = Math.min(Math.max(bounds.height, Math.min(520, area.height)), area.height)
  const x = Math.max(area.x, Math.min(bounds.x, area.x + area.width - width))
  const y = Math.max(area.y, Math.min(bounds.y, area.y + area.height - height))
  if (x !== bounds.x || y !== bounds.y || width !== bounds.width || height !== bounds.height) {
    win.setBounds({ x, y, width, height })
  }
}

export function createAppWindow(geometry: Pick<BrowserWindowConstructorOptions, 'width' | 'height' | 'minWidth' | 'minHeight'>): BrowserWindow {
  const iconPath = resolveIconPath()
  return new BrowserWindow({
    ...geometry,
    show: false,
    title: 'Novalist',
    ...(iconPath ? { icon: iconPath } : {}),
    ...materialWindowOptions(material),
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: false,
      contextIsolation: true,
      nodeIntegration: false,
      additionalArguments: [`--nl-material=${material}`]
    }
  })
}
