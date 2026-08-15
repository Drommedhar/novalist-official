import { type BrowserWindowConstructorOptions } from 'electron'

export type Material = 'glass' | 'vibrancy' | 'opaque'

/**
 * Picks the chrome material for this OS. True Liquid Glass (NSGlassEffectView)
 * needs macOS 26+; older macOS gets NSVisualEffectView vibrancy; Windows and
 * Linux render opaque themed surfaces from the same design tokens.
 */
export function detectMaterial(platform: NodeJS.Platform, osVersion: string): Material {
  if (platform !== 'darwin') return 'opaque'
  const major = Number.parseInt(osVersion.split('.')[0] ?? '', 10)
  // An unreadable version still means macOS, so fall back to vibrancy rather
  // than 'opaque': opaque now implies the Windows/Linux hidden title bar, which
  // on macOS would take the traffic lights with it.
  if (Number.isNaN(major)) return 'vibrancy'
  return major >= 26 ? 'glass' : 'vibrancy'
}

/**
 * Window options that let the native material show through behind the renderer.
 *
 * Windows and Linux used to hide the native title bar and let the renderer's
 * own toolbar be the window chrome, with the system window controls overlaid at
 * its right edge and the menu bar reachable only by pressing Alt. It looked
 * better and it cost the app its index: the menu bar is the one surface that
 * can list everything without being in the way, and the one every writer
 * already knows how to read. A hidden index is not an index.
 *
 * So the native title bar and menu bar are back, and the toolbar is an ordinary
 * strip below them. On macOS the system menu bar was always there, so nothing
 * changes.
 */
export function materialWindowOptions(material: Material): BrowserWindowConstructorOptions {
  if (material === 'opaque') {
    return { autoHideMenuBar: false }
  }
  if (material === 'glass') {
    // NSGlassEffectView is attached post-load; vibrancy must stay off or it
    // overrides the glass and renders as plain blur.
    return { transparent: true, titleBarStyle: 'hiddenInset' }
  }
  return {
    vibrancy: 'sidebar',
    backgroundColor: '#00000000',
    titleBarStyle: 'hiddenInset'
  }
}

/** Attaches the native Liquid Glass view on macOS 26+; safe no-op elsewhere. */
export function attachLiquidGlass(win: import('electron').BrowserWindow): void {
  void import('electron-liquid-glass')
    .then(({ default: liquidGlass }) => {
      liquidGlass.addView(win.getNativeWindowHandle(), {})
    })
    .catch((error: unknown) => {
      console.error('[glass] liquid glass unavailable:', error)
    })
}
