import { test, expect } from '@playwright/test'
import { spawn } from 'node:child_process'
import {
  chmodSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  writeFileSync
} from 'node:fs'
import { tmpdir } from 'node:os'
import { basename, join } from 'node:path'
import { launchApp, seedBook, type Harness } from './harness'
import { buildLinuxUpdateScript } from '../src/main/linuxAppImageUpdater'

type UpdateProbe = {
  openedPath: string | null
  quitCalls: number
  downloadStarted?: boolean
  releaseDownload?: (() => void) | null
  originalQuit: (() => void) | null
  originalFetch?: typeof fetch
  originalOpenPath?: (path: string) => Promise<string>
}

type RendererFlushProbe = {
  originalFlushPendingSave: () => Promise<void>
  release: () => void
}

type RendererPaneFlushProbe = {
  originalFlushPane: (paneId: string) => Promise<void>
  release: () => void
  blocked: boolean
}

async function installUpdateProbe(
  h: Harness,
  assetName: string,
  launchError = '',
  holdDownload = false
): Promise<void> {
  await h.app.evaluate(
    ({ app, shell }, options: { assetName: string; launchError: string; holdDownload: boolean }) => {
      const root = globalThis as typeof globalThis & { __updateProbe?: UpdateProbe }
      const state = (root.__updateProbe ??= {
        openedPath: null,
        quitCalls: 0,
        originalQuit: app.quit.bind(app),
        originalFetch: globalThis.fetch,
        originalOpenPath: shell.openPath.bind(shell)
      })
      state.openedPath = null
      state.quitCalls = 0
      state.downloadStarted = false
      state.releaseDownload = null
      app.quit = () => {
        state.quitCalls += 1
      }
      shell.openPath = async (path: string) => {
        state.openedPath = path
        return options.launchError
      }
      const payload = new TextEncoder().encode('novalist-update')
      globalThis.fetch = async (input) => {
        const url = String(input)
        if (url.includes('/releases/latest')) {
          return new Response(
            JSON.stringify({
              tag_name: 'v999.0.0',
              html_url: 'https://updates.test/release',
              body: 'Test update',
              assets: [{
                name: options.assetName,
                browser_download_url: 'https://updates.test/installer',
                size: payload.length
              }]
            }),
            { status: 200, headers: { 'content-type': 'application/json' } }
          )
        }
        if (url === 'https://updates.test/installer') {
          if (options.holdDownload) {
            state.downloadStarted = true
            await new Promise<void>((resolve) => {
              state.releaseDownload = resolve
            })
          }
          return new Response(payload, {
            status: 200,
            headers: { 'content-length': String(payload.length) }
          })
        }
        return new Response(null, { status: 404 })
      }
    },
    { assetName, launchError, holdDownload }
  )
}

async function restoreUpdateProbe(h: Harness): Promise<void> {
  await h.app.evaluate(({ app, shell }) => {
    const root = globalThis as typeof globalThis & { __updateProbe?: UpdateProbe }
    const state = root.__updateProbe
    if (!state) return
    state.releaseDownload?.()
    if (state.originalQuit) app.quit = state.originalQuit
    if (state.originalFetch) globalThis.fetch = state.originalFetch
    if (state.originalOpenPath) shell.openPath = state.originalOpenPath
    delete root.__updateProbe
  })
}

async function openUpdateDialog(h: Harness): Promise<void> {
  await h.page.evaluate(() => {
    window.novalistStores.extensions.setState({ checkStoreUpdates: async () => 0 })
    window.postMessage({ novalist: 'menu-command', command: 'help:checkUpdates' }, '*')
  })
  await expect(h.page.getByRole('dialog', { name: 'Update Available' })).toBeVisible({
    timeout: 15_000
  })
}

async function releaseRendererPaneFlush(h: Harness): Promise<void> {
  if (h.page.isClosed()) return
  await h.page.evaluate(() => {
    const root = globalThis as typeof globalThis & {
      __updatePaneFlushProbe?: RendererPaneFlushProbe
    }
    const probe = root.__updatePaneFlushProbe
    if (!probe) return
    window.novalistStores.project.setState({ flushPane: probe.originalFlushPane })
    probe.release()
    delete root.__updatePaneFlushProbe
  })
}

test('app update stages before installer launch and quits only after acknowledgement', async () => {
  test.skip(process.platform === 'linux', 'Linux uses the detached AppImage handoff tested below')
  const h = await launchApp('novalist-app-update-')
  const assetName = `novalist-update-test-${Date.now()}.exe`

  try {
    await h.app.evaluate(({ app, shell }) => {
      const root = globalThis as typeof globalThis & { __updateProbe?: UpdateProbe }
      const state = (root.__updateProbe ??= {
        openedPath: null,
        quitCalls: 0,
        originalQuit: null
      })
      state.originalQuit = app.quit.bind(app)
      app.quit = () => {
        state.quitCalls += 1
      }
      shell.openPath = async (path: string) => {
        state.openedPath = path
        return ''
      }
    })

    const update = {
      version: '999.0.0',
      tagName: 'v999.0.0',
      htmlUrl: '',
      notes: '',
      downloadUrl: 'data:application/octet-stream;base64,bm92YWxpc3QtdXBkYXRl',
      assetName,
      assetSize: 0
    }
    const result = await h.page.evaluate((info) => window.novalist.downloadAppUpdate(info), update)

    const downloaded = await h.app.evaluate(() => {
      const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
      const state = root.__updateProbe
      return { openedPath: state.openedPath, quitCalls: state.quitCalls }
    })
    expect(downloaded.openedPath).toBeNull()
    expect(downloaded.quitCalls).toBe(0)
    if (!result.launchToken) throw new Error('Expected an installer launch token')

    await h.page.evaluate((token) => window.novalist.launchAppUpdate(token), result.launchToken)
    const launched = await h.app.evaluate(() => {
      const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
      const state = root.__updateProbe
      return { openedPath: state.openedPath, quitCalls: state.quitCalls }
    })
    expect(basename(launched.openedPath ?? '')).toBe(assetName)
    // The authenticated launch IPC owns quit too; there is no second renderer
    // message that can be lost after a Linux helper has already started.
    expect(launched.quitCalls).toBe(1)

    await h.app.evaluate(({ shell }) => {
      const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
      const state = root.__updateProbe
      state.openedPath = null
      state.quitCalls = 0
      shell.openPath = async (path: string) => {
        state.openedPath = path
        return 'installer launch was blocked'
      }
    })

    const blockedDownload = await h.page.evaluate(
      (info) =>
        window.novalist.downloadAppUpdate({ ...info, assetName: `blocked-${info.assetName}` }),
      update
    )
    const beforeBlockedLaunch = await h.app.evaluate(() => {
      const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
      const state = root.__updateProbe
      return { openedPath: state.openedPath, quitCalls: state.quitCalls }
    })
    expect(beforeBlockedLaunch.openedPath).toBeNull()
    expect(beforeBlockedLaunch.quitCalls).toBe(0)
    if (!blockedDownload.launchToken) throw new Error('Expected an installer launch token')
    await expect(
      h.page.evaluate(
        (token) => window.novalist.launchAppUpdate(token),
        blockedDownload.launchToken
      )
    ).rejects.toThrow(/installer launch was blocked/)

    const blocked = await h.app.evaluate(() => {
      const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
      const state = root.__updateProbe
      return { openedPath: state.openedPath, quitCalls: state.quitCalls }
    })
    expect(basename(blocked.openedPath ?? '')).toBe(`blocked-${assetName}`)
    expect(blocked.quitCalls).toBe(0)
  } finally {
    await h.app.evaluate(({ app }) => {
      const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
      const state = root.__updateProbe
      if (state.originalQuit) app.quit = state.originalQuit
    })
    await h.close()
  }
})

test('main backend reconnects after the last detached pane closes', async () => {
  const h = await launchApp('novalist-detached-reconnect-')

  try {
    await seedBook(h, { One: ['A'] })
    const detachedOpened = h.app.waitForEvent('window')
    await h.page.evaluate(async () => {
      const project = window.novalistStores.project.getState()
      await window.novalist.openPaneWindow({
        view: 'dashboard',
        projectPath: project.projectPath,
        chapterGuid: null,
        sceneId: null
      })
    })
    const detached = await detachedOpened
    await expect(detached.locator('.app-shell.detached')).toBeVisible({ timeout: 30_000 })
    await detached.evaluate(() => window.novalistRpc.request('system/ping'))

    await detached.close()
    await expect.poll(() => h.app.windows().length).toBe(1)
    await expect.poll(() => h.rpc<{ pong: boolean }>('system/ping')).toMatchObject({ pong: true })
  } finally {
    await h.close()
  }
})

test('update button flushes live prose before acknowledging quit', async () => {
  test.skip(process.platform === 'linux', 'Linux uses the detached AppImage handoff tested below')
  const h = await launchApp('novalist-app-update-save-')
  const assetName = `novalist-update-save-${Date.now()}.exe`

  try {
    const book = await seedBook(h, { One: ['A'] })
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    await h.rpc('scenes/write', [chapter.guid, scene.id, '<p>Before.</p>', 'Before.'])
    await h.page.locator('.binder-scene-row').click()
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toBeVisible({ timeout: 30_000 })

    // Leave this inside the two-second project-store debounce. A cached update
    // used to close the backend before this write had a chance to run.
    await editor.evaluate((node) => {
      node.innerHTML = '<p>Words immediately before updating.</p>'
      node.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText' }))
    })

    await installUpdateProbe(h, assetName)
    await openUpdateDialog(h)
    await h.page.getByRole('button', { name: 'Download & Install' }).click()

    await expect
      .poll(() =>
        h.app.evaluate(() => {
          const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
          return root.__updateProbe.quitCalls
        })
      )
      .toBe(1)
    const stored = await h.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])
    expect(stored.html).toContain('Words immediately before updating.')
  } finally {
    await restoreUpdateProbe(h)
    await h.close()
  }
})

test('update stays locked and flushes edits made while the download is running', async () => {
  test.skip(process.platform === 'linux', 'Linux uses the detached AppImage handoff tested below')
  const h = await launchApp('novalist-app-update-download-save-')
  const assetName = `novalist-update-download-save-${Date.now()}.exe`

  try {
    const book = await seedBook(h, { One: ['A'] })
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    await h.page.locator('.binder-scene-row').click()
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toBeVisible({ timeout: 30_000 })

    await installUpdateProbe(h, assetName, '', true)
    await openUpdateDialog(h)
    await h.page.getByRole('button', { name: 'Download & Install' }).click()
    await expect
      .poll(() =>
        h.app.evaluate(() => {
          const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
          return root.__updateProbe.downloadStarted
        })
      )
      .toBe(true)

    const dialog = h.page.getByRole('dialog', { name: 'Update Available' })
    await expect(dialog.getByRole('button', { name: 'Later' })).toBeDisabled()
    await expect(dialog.getByRole('button', { name: 'View release' })).toBeDisabled()
    await dialog.press('Escape')
    await h.page.locator('.dialog-overlay').dispatchEvent('pointerdown')
    await expect(dialog).toBeVisible()

    // Hold the mounted editor's post-download flush itself. The installer must
    // remain unopened until this final save acknowledgement is released.
    await h.page.evaluate(() => {
      const root = globalThis as typeof globalThis & {
        __updatePaneFlushProbe?: RendererPaneFlushProbe
      }
      const project = window.novalistStores.project
      let release = (): void => {}
      const blocked = new Promise<void>((resolve) => {
        release = resolve
      })
      const probe: RendererPaneFlushProbe = {
        originalFlushPane: project.getState().flushPane,
        release,
        blocked: false
      }
      root.__updatePaneFlushProbe = probe
      project.setState({
        flushPane: async (paneId) => {
          probe.blocked = true
          await blocked
          await probe.originalFlushPane(paneId)
        }
      })
    })

    // Simulates background/forwarded editor work that lands after the first
    // preflight. The second preflight must still capture it before quitting.
    await h.page.evaluate(() => {
      const store = window.novalistStores.project.getState()
      const pane = store.activeEditorPaneId
      if (!pane) throw new Error('No active editor pane')
      store.onEditorContentChanged(
        pane,
        '<p>Words written while the update downloaded.</p>',
        'Words written while the update downloaded.'
      )
    })
    await expect
      .poll(() => h.page.evaluate(() => window.novalistStores.project.getState().isDirty))
      .toBe(true)

    await h.app.evaluate(() => {
      const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
      root.__updateProbe.releaseDownload?.()
    })
    await expect
      .poll(() =>
        h.page.evaluate(() => {
          const root = globalThis as typeof globalThis & {
            __updatePaneFlushProbe?: RendererPaneFlushProbe
          }
          return root.__updatePaneFlushProbe?.blocked ?? false
        })
      )
      .toBe(true)
    const beforeFinalSave = await h.app.evaluate(() => {
      const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
      return {
        openedPath: root.__updateProbe.openedPath,
        quitCalls: root.__updateProbe.quitCalls
      }
    })
    expect(beforeFinalSave.openedPath).toBeNull()
    expect(beforeFinalSave.quitCalls).toBe(0)
    await releaseRendererPaneFlush(h)
    await expect
      .poll(() =>
        h.app.evaluate(() => {
          const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
          return root.__updateProbe.quitCalls
        })
      )
      .toBe(1)
    const stored = await h.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])
    expect(stored.html).toContain('Words written while the update downloaded.')
  } finally {
    await releaseRendererPaneFlush(h)
    await restoreUpdateProbe(h)
    await h.close()
  }
})

test('update preflight times out when a save flush never settles', async () => {
  test.skip(process.platform === 'linux', 'Linux uses the detached AppImage handoff tested below')
  const h = await launchApp('novalist-app-update-save-timeout-')
  const assetName = `novalist-update-save-timeout-${Date.now()}.exe`

  try {
    await installUpdateProbe(h, assetName, '', true)
    await openUpdateDialog(h)
    const download = h.page.getByRole('button', { name: 'Download & Install' })
    await download.click()
    await expect
      .poll(() =>
        h.app.evaluate(() => {
          const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
          return root.__updateProbe.downloadStarted
        })
      )
      .toBe(true)

    // The initial preflight has succeeded and the asset is downloaded as soon
    // as the held response is released. Hang only the post-download save fence.
    await h.page.evaluate(() => {
      const root = globalThis as typeof globalThis & { __updateFlushProbe?: RendererFlushProbe }
      const project = window.novalistStores.project
      let release = (): void => {}
      const blocked = new Promise<void>((resolve) => {
        release = resolve
      })
      root.__updateFlushProbe = {
        originalFlushPendingSave: project.getState().flushPendingSave,
        release
      }
      project.setState({
        flushPendingSave: () => blocked
      })
    })
    await h.app.evaluate(() => {
      const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
      root.__updateProbe.releaseDownload?.()
    })

    await expect(h.page.getByRole('alert')).toContainText(
      'Another project operation is still running',
      { timeout: 15_000 }
    )
    await expect(download).toBeEnabled()
    const probe = await h.app.evaluate(() => {
      const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
      return {
        openedPath: root.__updateProbe.openedPath,
        quitCalls: root.__updateProbe.quitCalls
      }
    })
    expect(probe.openedPath).toBeNull()
    expect(probe.quitCalls).toBe(0)
  } finally {
    await h.page.evaluate(() => {
      const root = globalThis as typeof globalThis & { __updateFlushProbe?: RendererFlushProbe }
      const probe = root.__updateFlushProbe
      if (!probe) return
      window.novalistStores.project.setState({
        flushPendingSave: probe.originalFlushPendingSave
      })
      probe.release()
      delete root.__updateFlushProbe
    })
    await restoreUpdateProbe(h)
    await h.close()
  }
})

test('update stays open when the final scene save finds a conflict', async () => {
  test.skip(process.platform === 'linux', 'Linux uses the detached AppImage handoff tested below')
  const h = await launchApp('novalist-app-update-conflict-')
  const assetName = `novalist-update-conflict-${Date.now()}.exe`

  try {
    const book = await seedBook(h, { One: ['A'] })
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    await h.page.locator('.binder-scene-row').click()
    const editor = h.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toBeVisible({ timeout: 30_000 })

    // Simulate a synced copy landing after this editor read its scene hash.
    await h.rpc('scenes/write', [
      chapter.guid,
      scene.id,
      '<p>Words from the other machine.</p>',
      'Words from the other machine.'
    ])
    await editor.evaluate((node) => {
      node.innerHTML = '<p>My unresolved words.</p>'
      node.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText' }))
    })

    await installUpdateProbe(h, assetName)
    await openUpdateDialog(h)
    const download = h.page.getByRole('button', { name: 'Download & Install' })
    await download.click()

    await expect(h.page.getByRole('alert')).toContainText('unresolved save conflict')
    await expect(download).toBeEnabled()
    const probe = await h.app.evaluate(() => {
      const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
      return {
        openedPath: root.__updateProbe.openedPath,
        quitCalls: root.__updateProbe.quitCalls
      }
    })
    expect(probe.openedPath).toBeNull()
    expect(probe.quitCalls).toBe(0)
    const stored = await h.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])
    expect(stored.html).toContain('Words from the other machine.')
  } finally {
    await restoreUpdateProbe(h)
    await h.close()
  }
})

test('installer launch failure stays visible and leaves the app running', async () => {
  test.skip(process.platform === 'linux', 'Linux uses the detached AppImage handoff tested below')
  const h = await launchApp('novalist-app-update-error-')
  const assetName = `novalist-update-error-${Date.now()}.exe`

  try {
    await installUpdateProbe(h, assetName, 'installer launch was blocked')
    await openUpdateDialog(h)
    const download = h.page.getByRole('button', { name: 'Download & Install' })
    await download.click()

    await expect(h.page.getByRole('alert')).toContainText('installer launch was blocked')
    await expect(download).toBeEnabled()
    const probe = await h.app.evaluate(() => {
      const root = globalThis as typeof globalThis & { __updateProbe: UpdateProbe }
      return {
        openedPath: root.__updateProbe.openedPath,
        quitCalls: root.__updateProbe.quitCalls
      }
    })
    expect(basename(probe.openedPath ?? '')).toBe(assetName)
    expect(probe.quitCalls).toBe(0)
  } finally {
    await restoreUpdateProbe(h)
    await h.close()
  }
})

test('Linux AppImage handoff waits, backs up, rolls back, and reports status', () => {
  const script = buildLinuxUpdateScript(
    "/tmp/new Novalist's.AppImage",
    '/opt/Novalist.AppImage',
    4242
  )

  expect(script).toContain('while kill -0 4242 2>/dev/null; do sleep 0.2; done')
  expect(script).toContain("DOWNLOADED='/tmp/new Novalist'\\''s.AppImage'")
  expect(script).toContain("INSTALLED='/opt/Novalist.AppImage'")
  expect(script).toContain('STAGED="${INSTALLED}.novalist-update.$$"')
  expect(script).toContain('cp -fp -- "$DOWNLOADED" "$STAGED"')
  expect(script).toContain('cmp -s -- "$DOWNLOADED" "$STAGED"')
  expect(script).toContain('cp -fp -- "$INSTALLED" "$BACKUP"')
  expect(script).toContain('mv -f -- "$STAGED" "$INSTALLED"')
  expect(script).toContain('if restore_previous; then')
  expect(script).toContain('stage=relaunch result=rolled-back')
  expect(script).toContain('setsid nohup "$1"')
  expect(script).toContain('trap \'rm -f -- "$0" "$STAGED"\' EXIT')

  const failedReplace = script.slice(
    script.indexOf('if ! mv -f -- "$STAGED" "$INSTALLED"'),
    script.indexOf('if ! chmod +x -- "$INSTALLED"')
  )
  expect(failedReplace.indexOf('restore_previous')).toBeLessThan(
    failedReplace.indexOf('launch_target "$DOWNLOADED"')
  )
})

test('Linux AppImage helper replaces and relaunches a real executable', async () => {
  test.skip(process.platform !== 'linux', 'Requires bash and POSIX executable permissions')
  test.setTimeout(20_000)
  const root = mkdtempSync(join(tmpdir(), 'novalist-appimage-update-'))
  const installed = join(root, 'Novalist.AppImage')
  const downloaded = join(root, 'Novalist-new.AppImage')
  const helper = join(root, 'handoff.sh')
  const marker = join(root, 'launched.txt')
  const previous = '#!/bin/bash\nsleep 5\n'
  const safeMarker = marker.replaceAll("'", "'\\''")
  const replacement = `#!/bin/bash\nprintf 'new' > '${safeMarker}'\nsleep 5\n`
  writeFileSync(installed, previous, { mode: 0o755 })
  writeFileSync(downloaded, replacement, { mode: 0o755 })

  const blocker = spawn('/bin/bash', ['-c', 'while :; do sleep 1; done'], { stdio: 'ignore' })
  writeFileSync(helper, buildLinuxUpdateScript(downloaded, installed, blocker.pid!), { mode: 0o755 })
  const handoff = spawn('/bin/bash', [helper], { stdio: 'ignore' })

  await new Promise((resolve) => setTimeout(resolve, 300))
  expect(existsSync(marker)).toBe(false)
  blocker.kill()
  await new Promise<void>((resolve, reject) => {
    handoff.once('error', reject)
    handoff.once('exit', (code) => (code === 0 ? resolve() : reject(new Error(`exit ${code}`))))
  })

  expect(readFileSync(installed, 'utf8')).toBe(replacement)
  expect(readFileSync(`${installed}.novalist-previous`, 'utf8')).toBe(previous)
  expect(readFileSync(marker, 'utf8')).toBe('new')
  expect(readFileSync(`${downloaded}.handoff.log`, 'utf8')).toContain('result=ok')
  expect(existsSync(downloaded)).toBe(false)
  expect(existsSync(helper)).toBe(false)
  chmodSync(installed, 0o755)
})

test('Linux AppImage helper restores the previous executable when the new one exits', async () => {
  test.skip(process.platform !== 'linux', 'Requires bash and POSIX executable permissions')
  test.setTimeout(20_000)
  const root = mkdtempSync(join(tmpdir(), 'novalist-appimage-rollback-'))
  const installed = join(root, 'Novalist.AppImage')
  const downloaded = join(root, 'Novalist-new.AppImage')
  const helper = join(root, 'handoff.sh')
  const marker = join(root, 'previous-launched.txt')
  const safeMarker = marker.replaceAll("'", "'\\''")
  const previous = `#!/bin/bash\nprintf 'old' > '${safeMarker}'\nsleep 5\n`
  const broken = '#!/bin/bash\nexit 1\n'
  writeFileSync(installed, previous, { mode: 0o755 })
  writeFileSync(downloaded, broken, { mode: 0o755 })
  // Linux PID limits are far below this value, so the wait loop is already
  // satisfied without creating a second process solely as a blocker.
  writeFileSync(helper, buildLinuxUpdateScript(downloaded, installed, 999_999_999), {
    mode: 0o755
  })
  const handoff = spawn('/bin/bash', [helper], { stdio: 'ignore' })

  await new Promise<void>((resolve, reject) => {
    handoff.once('error', reject)
    handoff.once('exit', (code) => (code === 1 ? resolve() : reject(new Error(`exit ${code}`))))
  })

  expect(readFileSync(installed, 'utf8')).toBe(previous)
  expect(readFileSync(marker, 'utf8')).toBe('old')
  expect(readFileSync(`${downloaded}.handoff.log`, 'utf8')).toContain('result=rolled-back')
  expect(existsSync(`${installed}.novalist-previous`)).toBe(false)
  expect(existsSync(helper)).toBe(false)
})

test('Linux AppImage helper restores the installed path when the atomic replace fails', async () => {
  test.skip(process.platform !== 'linux', 'Requires bash and POSIX executable permissions')
  test.setTimeout(20_000)
  const root = mkdtempSync(join(tmpdir(), 'novalist-appimage-replace-failure-'))
  const installed = join(root, 'Novalist.AppImage')
  const downloaded = join(root, 'Novalist-new.AppImage')
  const helper = join(root, 'handoff.sh')
  const fakeBin = join(root, 'bin')
  const oldMarker = join(root, 'previous-launched.txt')
  const newMarker = join(root, 'downloaded-launched.txt')
  const safeOldMarker = oldMarker.replaceAll("'", "'\\''")
  const safeNewMarker = newMarker.replaceAll("'", "'\\''")
  const previous = `#!/bin/bash\nprintf 'old' > '${safeOldMarker}'\nsleep 5\n`
  const replacement = `#!/bin/bash\nprintf 'new' > '${safeNewMarker}'\nsleep 5\n`
  writeFileSync(installed, previous, { mode: 0o755 })
  writeFileSync(downloaded, replacement, { mode: 0o755 })
  mkdirSync(fakeBin)
  writeFileSync(
    join(fakeBin, 'mv'),
    '#!/bin/bash\nfor arg in "$@"; do\n' +
      '  case "$arg" in *.novalist-update.*) exit 1 ;; esac\n' +
      'done\nexec /bin/mv "$@"\n',
    { mode: 0o755 }
  )
  writeFileSync(helper, buildLinuxUpdateScript(downloaded, installed, 999_999_999), {
    mode: 0o755
  })
  const handoff = spawn('/bin/bash', [helper], {
    stdio: 'ignore',
    env: { ...process.env, PATH: `${fakeBin}:${process.env.PATH ?? ''}` }
  })

  await new Promise<void>((resolve, reject) => {
    handoff.once('error', reject)
    handoff.once('exit', (code) => (code === 1 ? resolve() : reject(new Error(`exit ${code}`))))
  })

  expect(readFileSync(installed, 'utf8')).toBe(previous)
  expect(readFileSync(oldMarker, 'utf8')).toBe('old')
  expect(existsSync(newMarker)).toBe(false)
  expect(readFileSync(`${downloaded}.handoff.log`, 'utf8')).toContain(
    'stage=replace result=rolled-back'
  )
  expect(existsSync(downloaded)).toBe(true)
  expect(existsSync(`${installed}.novalist-previous`)).toBe(false)
  expect(existsSync(helper)).toBe(false)
})
