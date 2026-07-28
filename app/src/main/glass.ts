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
 * Ink Night and Parchment, from the identity. Used for the Windows/Linux window
 * controls before the renderer has booted and can report the live theme; the
 * renderer replaces them through setTitleBarOverlay once tokens resolve.
 */
export const DEFAULT_TITLE_BAR_OVERLAY = {
  color: '#0f1219',
  symbolColor: '#ece5d2',
  // The renderer's .toolbar is 38px tall including its 1px bottom border
  // (border-box). The overlay is an opaque native surface painted over the web
  // content, so matching 38 here covered that border and the hairline under the
  // toolbar visibly stopped short of the window controls. Stop 1px above it and
  // the border runs the full width. Keep in step with .toolbar in shell.css.
  height: 37
} as const

/**
 * Window options that let the native material show through behind the renderer.
 *
 * Windows and Linux have no API for recolouring the native title bar or menu
 * bar, so instead of leaving a grey strip above a dark app they are dropped:
 * the renderer's own toolbar becomes the title bar (it already carries the drag
 * region), and the system-drawn window controls are painted to match through
 * titleBarOverlay. The menu stays reachable on Alt.
 */
export function materialWindowOptions(material: Material): BrowserWindowConstructorOptions {
  if (material === 'opaque') {
    return {
      titleBarStyle: 'hidden',
      titleBarOverlay: { ...DEFAULT_TITLE_BAR_OVERLAY },
      autoHideMenuBar: true
    }
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
