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
  if (Number.isNaN(major)) return 'opaque'
  return major >= 26 ? 'glass' : 'vibrancy'
}

/** Window options that let the native material show through behind the renderer. */
export function materialWindowOptions(material: Material): BrowserWindowConstructorOptions {
  if (material === 'opaque') return {}
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
