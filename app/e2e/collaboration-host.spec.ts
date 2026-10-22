import { test, expect, _electron as electron } from '@playwright/test'
import { build } from 'esbuild'
import { mkdir, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'

interface HostTestEnvironment {
  DocumentHostProcess: typeof import('../src/main/collaboration/DocumentHostProcess').DocumentHostProcess
  DesktopDocumentBridge: typeof import('../src/main/collaboration/DesktopDocumentBridge').DesktopDocumentBridge
  Y: typeof import('yjs')
}

test('the desktop document process retains deleted-scene recovery across a restart', async () => {
  const output = resolve('out/collaboration-process-tests')
  await mkdir(output, { recursive: true })
  await build({ entryPoints: {
    client: 'src/main/collaboration/DocumentHostProcess.ts',
    bridge: 'src/main/collaboration/DesktopDocumentBridge.ts',
    worker: 'src/main/collaboration/documentWorker.ts'
  }, outdir: output, outExtension: { '.js': '.cjs' }, bundle: true, packages: 'external', platform: 'node', format: 'cjs' })
  const fixture = resolve(output, 'main.cjs')
  await writeFile(fixture, "globalThis.documentHostTest = { DocumentHostProcess: require('./client.cjs').DocumentHostProcess, DesktopDocumentBridge: require('./bridge.cjs').DesktopDocumentBridge, Y: require('yjs') };\nrequire('electron').app.whenReady().then(() => {});\n")
  await writeFile(resolve(output, 'stopped.cjs'), 'process.exit(17);\n')
  const env: Record<string, string> = {}
  for (const [key, value] of Object.entries(process.env)) {
    if (value !== undefined && key !== 'ELECTRON_RUN_AS_NODE') env[key] = value
  }
  const application = await electron.launch({ args: [fixture], env })
  try {
    const actual = await application.evaluate(async ({ app }, paths) => {
      await app.whenReady()
      const { DocumentHostProcess, Y } = (globalThis as typeof globalThis & { documentHostTest: HostTestEnvironment }).documentHostTest
      const first = new DocumentHostProcess(paths.worker)
      let saved
      try {
        await first.execute({ kind: 'seed', id: 'scene-a', html: '<p>Original story.</p>' })
        const original = await first.capture('scene-a')
        const rejected = await first.execute({ kind: 'receive', id: 'scene-a', state: new Uint8Array([255]) })
        if (rejected?.accepted !== false || rejected.rejected.length !== 1 || rejected.rejected[0].length !== 64)
          throw new Error('Malformed input was not identified for quarantine.')
        await first.execute({ kind: 'validate', id: 'scene-a', state: original.state })
        const writer = new Y.Doc()
        try {
          Y.applyUpdate(writer, original.state)
          writer.getText('prose').insert(0, 'Shared ')
          await first.execute({ kind: 'remove', id: 'scene-a', state: original.state })
          await first.execute({ kind: 'receive', id: 'scene-a', state: Y.encodeStateAsUpdate(writer) })
          saved = await first.capture('scene-a')
        } finally { writer.destroy() }
      } finally { await first.dispose() }

      const restarted = new DocumentHostProcess(paths.worker)
      try {
        await restarted.execute({ kind: 'load', id: 'scene-a', state: saved.state, deletions: saved.deletions })
        const loaded = await restarted.capture('scene-a')
        return { html: loaded.html, recovered: loaded.recovered, deletions: loaded.deletions.length }
      } finally { await restarted.dispose() }
    }, { worker: resolve(output, 'worker.cjs') })
    expect(actual).toEqual({ html: null, recovered: '<p>Shared Original story.</p>', deletions: 1 })
    const bridged = await application.evaluate(async (_, worker) => {
      const { DesktopDocumentBridge } = (globalThis as typeof globalThis & { documentHostTest: HostTestEnvironment }).documentHostTest
      const bridge = new DesktopDocumentBridge(worker)
      const method = 'collaboration/documents'
      try {
        await bridge.request(method, { sessionId: 'first', action: 'open' })
        await bridge.request(method, { sessionId: 'first', action: 'execute', command: { kind: 'seed', id: 'scene-a', html: '<p>First session.</p>' } })
        const captured = await bridge.request(method, { sessionId: 'first', action: 'capture', id: 'scene-a' })
        await bridge.request(method, { sessionId: 'second', action: 'open' })
        let isolated = false
        try { await bridge.request(method, { sessionId: 'second', action: 'capture', id: 'scene-a' }) }
        catch { isolated = true }
        await bridge.reset()
        let closed = false
        try { await bridge.request(method, { sessionId: 'first', action: 'capture', id: 'scene-a' }) }
        catch { closed = true }
        return { captured, isolated, closed }
      } finally { await bridge.reset() }
    }, resolve(output, 'worker.cjs'))
    expect(bridged).toMatchObject({ captured: { html: '<p>First session.</p>', recovered: null, deletions: [], state: expect.any(String) }, isolated: true, closed: true })
    const immediateClose = await application.evaluate(async (_, worker) => {
      const { DocumentHostProcess } = (globalThis as typeof globalThis & { documentHostTest: HostTestEnvironment }).documentHostTest
      const host = new DocumentHostProcess(worker)
      return Promise.race([host.dispose().then(() => true), new Promise<boolean>(resolve => setTimeout(() => resolve(false), 2_000))])
    }, resolve(output, 'worker.cjs'))
    expect(immediateClose).toBe(true)
    const processFailure = await application.evaluate(async (_, worker) => {
      const { DocumentHostProcess } = (globalThis as typeof globalThis & { documentHostTest: HostTestEnvironment }).documentHostTest
      const host = new DocumentHostProcess(worker)
      let timer: ReturnType<typeof setTimeout> | undefined
      try {
        return await Promise.race([
          host.capture('scene-a').then(() => 'unexpected success', (error: Error) => error.message),
          new Promise<string>(resolve => { timer = setTimeout(() => resolve('timed out'), 5_000) })
        ])
      } finally { clearTimeout(timer); await host.dispose() }
    }, resolve(output, 'stopped.cjs'))
    expect(processFailure).toMatch(/^The shared document process stopped\./)
    expect(await application.evaluate(() => 'desktop remains alive')).toBe('desktop remains alive')
  } finally { await application.close() }
})
