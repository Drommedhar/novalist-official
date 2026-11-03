import { test, expect, _electron as electron } from '@playwright/test'
import { build } from 'esbuild'
import { mkdir, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'

interface HostTestEnvironment {
  DocumentHostProcess: typeof import('../src/main/collaboration/DocumentHostProcess').DocumentHostProcess
  DesktopDocumentBridge: typeof import('../src/main/collaboration/DocumentHostProcess').DesktopDocumentBridge
  Y: typeof import('yjs')
}

test('the desktop document process retains deleted-scene recovery across a restart', async () => {
  const output = resolve('out/collaboration-process-tests')
  await mkdir(output, { recursive: true })
  await build({ entryPoints: {
    client: 'src/main/collaboration/DocumentHostProcess.ts',
    worker: 'src/main/collaboration/documentWorker.ts'
  }, outdir: output, outExtension: { '.js': '.cjs' }, bundle: true, packages: 'external', platform: 'node', format: 'cjs' })
  const fixture = resolve(output, 'main.cjs')
  await writeFile(fixture, "globalThis.documentHostTest = { DocumentHostProcess: require('./client.cjs').DocumentHostProcess, DesktopDocumentBridge: require('./client.cjs').DesktopDocumentBridge, Y: require('yjs') };\nrequire('electron').app.whenReady().then(() => {});\n")
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
    const prose = await application.evaluate(async (_, worker) => {
      const { DocumentHostProcess, Y } = (globalThis as typeof globalThis & { documentHostTest: HostTestEnvironment }).documentHostTest
      const host = new DocumentHostProcess(worker)
      const writers = [new Y.Doc(), new Y.Doc()]
      try {
        const original = '**Literal** <tag> & text\n\nSecond line.\n'
        await host.execute({ kind: 'seedText', id: 'description', text: original })
        const seeded = await host.capture('description')
        for (const writer of writers) Y.applyUpdate(writer, seeded.state)
        writers[0].getText('prose').insert(0, 'Alice ')
        writers[1].getText('prose').insert(original.length, 'Bob')
        for (const writer of writers) await host.execute({ kind: 'receive', id: 'description', state: Y.encodeStateAsUpdate(writer) })
        const merged = await host.capture('description')
        writers[0].getText('prose').format(0, 1, { bold: 'true' })
        const invalid = await host.execute({ kind: 'receive', id: 'description', state: Y.encodeStateAsUpdate(writers[0]) })
        return { text: merged.text, rejected: invalid?.accepted === false }
      } finally { for (const writer of writers) writer.destroy(); await host.dispose() }
    }, resolve(output, 'worker.cjs'))
    expect(prose).toEqual({ text: 'Alice **Literal** <tag> & text\n\nSecond line.\nBob', rejected: true })
    const renamed = await application.evaluate(async (_, worker) => {
      const { DesktopDocumentBridge, Y } = (globalThis as typeof globalThis & { documentHostTest: HostTestEnvironment }).documentHostTest
      const bridge = new DesktopDocumentBridge(worker)
      const call = (params: object): Promise<unknown> => bridge.handle('collaboration/documents', { sessionId: 'rename', ...params })
      const capture = async (id: string): Promise<{ html: string; text: string; state: string }> =>
        await call({ action: 'capture', id }) as { html: string; text: string; state: string }
      const writer = new Y.Doc()
      try {
        await call({ action: 'open' })
        const html = '<p>Before <b>bold</b> <span class="nv-entity-mention" data-entity-id="person">Old</span> ' +
          '<span class="nv-entity-mention" data-entity-id="person" data-mention-source="alias">O</span> ' +
          '<span class="nv-entity-mention" data-entity-id="person" data-mention-source="manual">my wording</span> Old.</p>'
        await call({ action: 'execute', command: { kind: 'seed', id: 'scene', html } })
        const before = await capture('scene')
        Y.applyUpdate(writer, Buffer.from(before.state, 'base64'))
        writer.getText('prose').insert(0, 'Concurrent ')
        const rename = { action: 'rename', id: 'scene', entityId: 'person', oldName: 'Old', newName: 'New & name' }
        const update = await call(rename)
        if (typeof update !== 'string') throw new Error('Missing rename update.')
        const unchanged = (await capture('scene')).html === html
        await call({ action: 'execute', command: { kind: 'receive', id: 'scene', state: Buffer.from(Y.encodeStateAsUpdate(writer)).toString('base64') } })
        await call({ action: 'execute', command: { kind: 'receive', id: 'scene', state: update } })
        const scene = (await capture('scene')).html
        const repeated = await call(rename)
        await call({ action: 'execute', command: { kind: 'seedText', id: 'section', text: 'Old [[ Old ]] [[old|my words]] [[Other]]' } })
        const linkUpdate = await call({ ...rename, id: 'section' })
        await call({ action: 'execute', command: { kind: 'receive', id: 'section', state: linkUpdate } })
        const section = (await capture('section')).text
        const current = await capture('scene')
        await call({ action: 'execute', command: { kind: 'remove', id: 'scene', state: current.state } })
        const deleted = await call({ ...rename, newName: 'Later' })
        return { unchanged, scene, repeated, section, deleted }
      } finally { writer.destroy(); await bridge.reset() }
    }, resolve(output, 'worker.cjs'))
    expect(renamed).toEqual({
      unchanged: true,
      scene: '<p>Concurrent Before <b>bold</b> <span class="nv-entity-mention" data-entity-id="person">New &amp; name</span> ' +
        '<span class="nv-entity-mention" data-entity-id="person" data-mention-source="alias">O</span> ' +
        '<span class="nv-entity-mention" data-entity-id="person" data-mention-source="manual">my wording</span> Old.</p>',
      repeated: null, section: 'Old [[New & name]] [[New & name|my words]] [[Other]]', deleted: null
    })
    const bridged = await application.evaluate(async (_, worker) => {
      const { DesktopDocumentBridge } = (globalThis as typeof globalThis & { documentHostTest: HostTestEnvironment }).documentHostTest
      const bridge = new DesktopDocumentBridge(worker)
      const method = 'collaboration/documents'
      try {
        await bridge.handle(method, { sessionId: 'first', action: 'open' })
        await bridge.handle(method, { sessionId: 'first', action: 'execute', command: { kind: 'seed', id: 'scene-a', html: '<p>First session.</p>' } })
        const captured = await bridge.handle(method, { sessionId: 'first', action: 'capture', id: 'scene-a' })
        await bridge.handle(method, { sessionId: 'second', action: 'open' })
        let isolated = false
        try { await bridge.handle(method, { sessionId: 'second', action: 'capture', id: 'scene-a' }) }
        catch { isolated = true }
        await bridge.reset()
        let closed = false
        try { await bridge.handle(method, { sessionId: 'first', action: 'capture', id: 'scene-a' }) }
        catch { closed = true }
        return { captured, isolated, closed }
      } finally { await bridge.reset() }
    }, resolve(output, 'worker.cjs'))
    expect(bridged).toMatchObject({ captured: { html: '<p>First session.</p>', recovered: null, deletions: [], state: expect.any(String) }, isolated: true, closed: true })
    await application.evaluate(async (_, worker) => {
      const { DocumentHostProcess } = (globalThis as typeof globalThis & { documentHostTest: HostTestEnvironment }).documentHostTest
      const host = new DocumentHostProcess(worker)
      await host.dispose()
    }, resolve(output, 'worker.cjs'))
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
