const { execFileSync } = require('node:child_process')
const { join } = require('node:path')

/**
 * Ad-hoc code-sign the macOS .app for UNSIGNED local builds only.
 *
 * When a real certificate is available (CSC_LINK set in CI), electron-builder
 * signs the bundle properly with the Developer ID / Apple Distribution identity
 * after this hook runs, and notarizes it — so we must NOT ad-hoc sign here, or
 * the `--deep` pseudo-signature would fight the real one. We detect that case
 * and bail out.
 *
 * With no certificate (a local `npm run package`), electron-builder skips
 * signing and the bundle keeps only Electron's linker-signed stub (Identifier
 * "Electron"). Once macOS sets the download quarantine attribute, Gatekeeper
 * reports that as "damaged". A full ad-hoc re-sign of the whole bundle
 * (codesign --sign -) makes Gatekeeper accept the otherwise-unsigned build.
 */
exports.default = async function afterPack(context) {
  if (context.electronPlatformName !== 'darwin') return
  // A real signing identity is configured — let electron-builder do the signing.
  if (process.env.CSC_LINK || process.env.CSC_NAME) return
  const app = join(context.appOutDir, `${context.packager.appInfo.productFilename}.app`)
  execFileSync('codesign', ['--force', '--deep', '--sign', '-', app], { stdio: 'inherit' })
  console.log(`[afterPack] ad-hoc signed ${app}`)
}
