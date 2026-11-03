import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const app = fileURLToPath(new URL('..', import.meta.url))

function runFixture(env = {}, args = []) {
  const directory = mkdtempSync(join(tmpdir(), 'novalist-e2e-guard-'))
  const marker = join(directory, 'test-ran')
  const config = join(directory, 'playwright.config.ts')
  writeFileSync(config, `
    import config from ${JSON.stringify(join(app, 'playwright.config.ts'))}
    export default { ...config, testDir: '.', reporter: 'line', outputDir: ${JSON.stringify(join(directory, 'results'))} }
  `)
  // This exercises Playwright's real setup/worker boundary without starting Electron.
  writeFileSync(join(directory, 'sentinel.spec.ts'), `
    import { test } from ${JSON.stringify(join(app, 'node_modules/@playwright/test/index.js'))}
    import { writeFileSync } from 'node:fs'
    test('sentinel', () => writeFileSync(${JSON.stringify(marker)}, 'executed'))
  `)
  try {
    const environment = { ...process.env }
    delete environment.NOVALIST_E2E_ALLOW_DESKTOP
    delete environment.CI
    const result = spawnSync(process.execPath, [
      join(app, 'node_modules/@playwright/test/cli.js'), 'test', '--config', config, ...args
    ], { cwd: app, env: { ...environment, ...env }, encoding: 'utf8', timeout: 30_000 })
    assert.ifError(result.error)
    return { status: result.status, output: result.stdout + result.stderr, ran: existsSync(marker) }
  } finally {
    rmSync(directory, { recursive: true, force: true })
  }
}

test('E2E execution stops before any test runs without desktop permission', () => {
  const result = runFixture()
  assert.equal(result.status, 1, result.output)
  assert.equal(result.ran, false)
  assert.match(result.output, /NOVALIST_E2E_ALLOW_DESKTOP=1/)
})

test('a generic CI variable cannot silently authorize taking over the desktop', () => {
  const result = runFixture({ CI: 'true' })
  assert.equal(result.status, 1, result.output)
  assert.equal(result.ran, false)
  assert.match(result.output, /NOVALIST_E2E_ALLOW_DESKTOP=1/)
})

test('explicit permission lets isolated runners execute the real test body', () => {
  const result = runFixture({ NOVALIST_E2E_ALLOW_DESKTOP: '1' })
  assert.equal(result.status, 0, result.output)
  assert.equal(result.ran, true)
})

test('listing E2E tests is safe without desktop permission', () => {
  const result = runFixture({}, ['--list'])
  assert.equal(result.status, 0, result.output)
  assert.equal(result.ran, false)
  assert.match(result.output, /sentinel/)
})
