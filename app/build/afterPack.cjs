const { execFileSync } = require('node:child_process')
const { join } = require('node:path')

/**
 * Ad-hoc code-sign the macOS .app. electron-builder is configured with
 * identity:null, so it skips signing and the bundle keeps only Electron's
 * default linker-signed stub (Identifier "Electron"). Once macOS sets the
 * download quarantine attribute, Gatekeeper reports that as "damaged". A full
 * ad-hoc re-sign of the whole bundle (codesign --sign -) — the same pseudo
 * signing the old pipeline used — makes Gatekeeper accept the unsigned build.
 */
exports.default = async function afterPack(context) {
  if (context.electronPlatformName !== 'darwin') return
  const app = join(context.appOutDir, `${context.packager.appInfo.productFilename}.app`)
  execFileSync('codesign', ['--force', '--deep', '--sign', '-', app], { stdio: 'inherit' })
  console.log(`[afterPack] ad-hoc signed ${app}`)
}
