import assert from 'node:assert/strict'
import { EventEmitter } from 'node:events'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { dirname, join } from 'node:path'
import { runInNewContext } from 'node:vm'
import test from 'node:test'

const require = createRequire(import.meta.url)
const loader = readFileSync(join(dirname(require.resolve('playwright-core/package.json')), 'lib/server/electron/loader.js'), 'utf8')
const entry = readFileSync(new URL('../e2e/launch-app.cjs', import.meta.url), 'utf8')

for (const platform of ['linux', 'darwin', 'win32']) {
  test(`the ${platform} E2E entry runs after Playwright switches and before app startup`, () => {
    const switches = new Map()
    let loaded = false
    const app = Object.assign(new EventEmitter(), {
      commandLine: {
        appendSwitch: (name, value) => switches.set(name, value),
        removeSwitch: name => switches.delete(name)
      },
      whenReady: async () => {}
    })
    const context = {
      process: { platform, env: {}, argv: ['electron', '--inspect=0', '--remote-debugging-port=0', 'e2e/launch-app.cjs'] },
      require(name) {
        if (name === 'electron') return { app }
        assert.equal(name, '../out/main/index.js')
        assert.equal(app.isReady(), false, 'restore platform storage before Electron becomes ready')
        assert.equal(switches.get('password-store'), platform === 'linux' ? undefined : 'basic')
        assert.equal(switches.get('use-mock-keychain'), '', 'do not change other Playwright platform isolation')
        loaded = true
      }
    }
    runInNewContext(loader, context)
    assert.equal(switches.get('password-store'), 'basic', 'exercise the installed Playwright override')
    runInNewContext(entry, context)
    assert.equal(loaded, true, 'the wrapper must load the real app')
  })
}
