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
  return {
    // Liquid Glass attachment happens post-create (electron-liquid-glass, M6);
    // until then macOS 26 also runs on the vibrancy path.
    vibrancy: 'sidebar',
    backgroundColor: '#00000000',
    titleBarStyle: 'hiddenInset'
  }
}
