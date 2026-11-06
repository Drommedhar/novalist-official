const assert = require('node:assert/strict')
const { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } = require('node:fs')
const { tmpdir } = require('node:os')
const { join } = require('node:path')
const test = require('node:test')
const { runInNewContext } = require('node:vm')
const { Arch } = require('builder-util')

function fixture(t, { platform = 'mas', arch = Arch.arm64, signed = true, libraries = {} } = {}) {
  const appOutDir = mkdtempSync(join(tmpdir(), 'novalist-after-pack-'))
  t.after(() => rmSync(appOutDir, { recursive: true, force: true }))
  const backend = join(appOutDir, 'Novalist.app', 'Contents', 'Resources', 'backend')
  mkdirSync(backend, { recursive: true })
  for (const [name, architectures] of Object.entries(libraries)) {
    writeFileSync(join(backend, name), architectures)
  }
  const commands = []
  const context = {
    exports: {},
    process: { env: signed ? { CSC_LINK: 'distribution.p12' } : {} },
    console: { log() {} },
    require(name) {
      if (name !== 'node:child_process') return require(name)
      return {
        execFileSync(file, args) {
          commands.push([file, ...args])
          if (file === 'codesign') return
          assert.equal(file, 'lipo')
          if (args[0] === '-archs') return readFileSync(args[1], 'utf8')
          const binary = args[0]
          assert.equal(args[1], '-thin')
          assert.equal(args[3], '-output')
          assert.equal(args[4], binary)
          assert.ok(readFileSync(binary, 'utf8').split(' ').includes(args[2]))
          writeFileSync(binary, args[2])
        }
      }
    }
  }
  runInNewContext(readFileSync(join(__dirname, '..', 'build', 'afterPack.cjs'), 'utf8'), context)
  const run = () => context.exports.default({
    appOutDir,
    electronPlatformName: platform,
    arch,
    packager: { appInfo: { productFilename: 'Novalist' } }
  })
  return { run, backend, commands }
}

test('signed MAS arm64 packages remove unused native library slices before signing', async t => {
  const f = fixture(t, { libraries: {
    'libsodium.dylib': 'x86_64 arm64 arm64e',
    'libSkiaSharp.dylib': 'x86_64 arm64',
    'libcoreclr.dylib': 'arm64',
    'Novalist.Backend.pdb': 'debug symbols'
  } })
  await f.run()
  for (const name of ['libsodium.dylib', 'libSkiaSharp.dylib', 'libcoreclr.dylib']) {
    assert.equal(readFileSync(join(f.backend, name), 'utf8'), 'arm64')
  }
  assert.equal(readFileSync(join(f.backend, 'Novalist.Backend.pdb'), 'utf8'), 'debug symbols')
  assert.equal(f.commands.filter(command => command.includes('-thin')).length, 2)
  assert.ok(f.commands.every(([file]) => file === 'lipo'), 'real signing belongs to electron-builder')
})

test('MAS x64 packaging selects x86_64 instead of the build host architecture', async t => {
  const f = fixture(t, { arch: Arch.x64, libraries: { 'libsodium.dylib': 'x86_64 arm64 arm64e' } })
  await f.run()
  assert.equal(readFileSync(join(f.backend, 'libsodium.dylib'), 'utf8'), 'x86_64')
})

test('MAS packaging rejects a native library missing the requested architecture', async t => {
  const f = fixture(t, { libraries: { 'libsodium.dylib': 'x86_64 arm64e' } })
  await assert.rejects(f.run(), /libsodium\.dylib.*arm64/)
  assert.equal(readFileSync(join(f.backend, 'libsodium.dylib'), 'utf8'), 'x86_64 arm64e')
})

test('unsupported MAS architectures fail before mutating native libraries', async t => {
  const f = fixture(t, { arch: Arch.universal })
  await assert.rejects(f.run(), /Unsupported MAS architecture/)
  assert.deepEqual(f.commands, [])
})

test('signed direct-download macOS packages leave native slices and signing to electron-builder', async t => {
  const f = fixture(t, { platform: 'darwin', libraries: { 'libsodium.dylib': 'x86_64 arm64 arm64e' } })
  await f.run()
  assert.deepEqual(f.commands, [])
})

test('unsigned direct-download macOS packages retain ad-hoc signing', async t => {
  const f = fixture(t, { platform: 'darwin', signed: false })
  await f.run()
  assert.equal(f.commands.length, 1)
  assert.deepEqual(f.commands[0].slice(0, 5), ['codesign', '--force', '--deep', '--sign', '-'])
})

test('other desktop platforms skip macOS tools', async t => {
  const f = fixture(t, { platform: 'win32', signed: false })
  await f.run()
  assert.deepEqual(f.commands, [])
})
