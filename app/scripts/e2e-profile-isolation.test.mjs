import assert from 'node:assert/strict'
import { readFileSync, readdirSync } from 'node:fs'
import { join, resolve, relative, isAbsolute } from 'node:path'
import { tmpdir } from 'node:os'
import { fileURLToPath } from 'node:url'
import { runInNewContext } from 'node:vm'
import test from 'node:test'

const directory = fileURLToPath(new URL('../e2e', import.meta.url))

// Evaluate only each launch's options, never import the spec or start Electron.
// This guards both old direct launches and shared helpers against silently
// falling back to the user's real Chromium profile and single-instance lock.
test('every Electron test launch explicitly isolates Chromium within its test directory', () => {
  let launches = 0
  for (const path of readdirSync(directory, { recursive: true }).filter(path => path.endsWith('.ts'))) {
    const source = readFileSync(join(directory, path), 'utf8')
    const calls = [...source.matchAll(/\belectron\.launch\((\{[\s\S]*?\})\)/g)]
    assert.equal(calls.length, [...source.matchAll(/\belectron\.launch\(/g)].length,
      `${path}: launch options must be auditable without starting Electron`)
    for (const call of calls) {
      launches++
      const location = `${path}:${source.slice(0, call.index).split('\n').length}`
      const profiles = ['run-one', 'run-two'].map(run => {
        const workDir = join(tmpdir(), 'novalist-profile-isolation-test', run)
        const options = runInNewContext(`(${call[1]})`, {
          join, resolve, workDir,
          process: { platform: process.platform },
          env: { NOVALIST_SETTINGS_DIR: join(workDir, 'settings') },
          fixture: join(workDir, 'main.cjs'),
          PACKAGED_BIN: join(workDir, 'packaged-app'),
          launchArguments: []
        })
        const flags = (options.args ?? []).filter(arg => arg.startsWith('--user-data-dir='))
        assert.equal(flags.length, 1, `${location}: expected one explicit Chromium profile`)
        const profile = flags[0].slice('--user-data-dir='.length)
        assert.ok(isAbsolute(profile), `${location}: profile must be absolute`)
        const pathWithinTest = relative(workDir, profile)
        assert.ok(pathWithinTest && !pathWithinTest.startsWith('..') && !isAbsolute(pathWithinTest),
          `${location}: profile must be inside this launch's test directory`)
        return profile
      })
      assert.notEqual(profiles[0], profiles[1], `${location}: separate runs must not share profiles`)
    }
  }
  assert.ok(launches > 0, 'the isolation audit must inspect Electron launch sites')
})
