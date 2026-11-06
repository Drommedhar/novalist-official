const { execFileSync } = require('node:child_process')
const { readdirSync } = require('node:fs')
const { join } = require('node:path')
const { Arch } = require('builder-util')

function thinMasBackend(context, app) {
  const architecture = { [Arch.arm64]: 'arm64', [Arch.x64]: 'x86_64' }[context.arch]
  if (!architecture) throw new Error(`Unsupported MAS architecture: ${Arch[context.arch]}`)
  const backend = join(app, 'Contents', 'Resources', 'backend')
  for (const name of readdirSync(backend)) {
    if (!name.endsWith('.dylib')) continue
    const binary = join(backend, name)
    const architectures = execFileSync('lipo', ['-archs', binary], { encoding: 'utf8' }).trim().split(/\s+/)
    if (!architectures.includes(architecture)) {
      throw new Error(`${binary} does not support ${architecture}; found ${architectures.join(', ')}`)
    }
    if (architectures.length === 1) continue
    // libsodium's RID-specific NuGet assets still contain arm64e. MAS requires
    // every bundled library to match the architectures supported by the app.
    execFileSync('lipo', [binary, '-thin', architecture, '-output', binary], { stdio: 'inherit' })
    const result = execFileSync('lipo', ['-archs', binary], { encoding: 'utf8' }).trim()
    if (result !== architecture) throw new Error(`${binary} still has unexpected architectures: ${result}`)
    console.log(`[afterPack] thinned ${name} to ${architecture}`)
  }
}

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
  const app = join(context.appOutDir, `${context.packager.appInfo.productFilename}.app`)
  if (context.electronPlatformName === 'mas') {
    thinMasBackend(context, app)
    return
  }
  if (context.electronPlatformName !== 'darwin') return
  // A real signing identity is configured — let electron-builder do the signing.
  if (process.env.CSC_LINK || process.env.CSC_NAME) return
  execFileSync('codesign', ['--force', '--deep', '--sign', '-', app], { stdio: 'inherit' })
  console.log(`[afterPack] ad-hoc signed ${app}`)
}
