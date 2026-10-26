import type { Page } from '@playwright/test'
import { spawn } from 'node:child_process'
import { createServer } from 'node:http'
import { mkdtempSync, mkdirSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { basename, extname, join, resolve, sep } from 'node:path'

/** The actual mobile bundle and HybridWebView shim, connected to the real
 * backend. Only the native file chooser/share sheet is represented by a fake. */
export async function mobileHost(page: Page, layout: 'phone' | 'tablet', platform: 'darwin' | 'android' = 'darwin') {
  const root = mkdtempSync(join(tmpdir(), 'nl-mobile-host-'))
  const bundle = resolve('../Novalist.Mobile/Resources/Raw/app')
  const backend = spawn('dotnet', [resolve('../Novalist.Backend/bin/Debug/net10.0/Novalist.Backend.dll')], {
    env: { ...process.env, NOVALIST_SETTINGS_DIR: join(root, 'settings') },
    stdio: ['pipe', 'pipe', 'pipe']
  })
  const backendClosed = new Promise<void>((done) => backend.once('close', () => done()))
  backend.stderr.resume()
  let deliveries = Promise.resolve()
  backend.stdout.on('data', (bytes: Buffer) => {
    deliveries = deliveries.then(() => page.evaluate((data) => {
      (window as unknown as { __novalistRecv(data: string): void }).__novalistRecv(data)
    }, bytes.toString('base64'))).catch(() => {})
  })
  const host = {
    root,
    outcome: 'success' as 'success' | 'cancel' | 'error' | 'handoff',
    failSave: false,
    failExport: false,
    shared: [] as { path: string; bytes: Buffer }[],
    released: [] as string[],
    created: [] as string[]
  }
  const server = createServer((req, res) => {
    if (req.url === '/_framework/hybridwebview.js') {
      res.setHeader('Content-Type', 'text/javascript')
      res.end('window.HybridWebView = { SendRawMessage: (message) => window.mobileSend(message) }')
      return
    }
    const path = resolve(bundle, '.' + decodeURIComponent((req.url ?? '/').split('?')[0]))
    if (!path.startsWith(bundle + sep)) { res.writeHead(403).end(); return }
    try {
      const types: Record<string, string> = {
        '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css',
        '.json': 'application/json', '.woff2': 'font/woff2', '.svg': 'image/svg+xml'
      }
      res.setHeader('Content-Type', types[extname(path)] ?? 'application/octet-stream')
      res.end(readFileSync(path))
    } catch { res.writeHead(404).end() }
  })
  await new Promise<void>((done) => server.listen(0, '127.0.0.1', done))
  await page.exposeFunction('mobileSend', async (raw: string) => {
    if (!raw.startsWith('{')) { backend.stdin.write(Buffer.from(raw, 'base64')); return }
    const call = JSON.parse(raw) as { id: number; method: string; args: unknown[] }
    let result: unknown = null
    let error: string | undefined
    try {
      switch (call.method) {
        case 'hostCapabilities': result = { platform, projectStorage: platform === 'android' ? 'private' : 'external', layout }; break
        case 'beginProjectAccess': result = true; break
        case 'saveFile': {
          if (host.failSave) throw new Error('Cannot allocate export')
          const dir = join(root, 'exports', String(host.created.length))
          mkdirSync(dir, { recursive: true })
          result = join(dir, basename(String(call.args[0])))
          host.created.push(result as string)
          if (host.failExport) mkdirSync(result as string)
          break
        }
        case 'shareExport': {
          const path = String(call.args[0])
          host.shared.push({ path, bytes: readFileSync(path) })
          if (host.outcome === 'error') throw new Error('Share failed')
          result = host.outcome === 'handoff' ? 'handed-off' : host.outcome === 'success'
          break
        }
        case 'releaseExport':
          host.released.push(String(call.args[0]))
          rmSync(String(call.args[0]), { force: true, recursive: true })
          break
        case 'requestLayout':
          await page.evaluate((mode) => {
            (window as unknown as { __novalistLayout(mode: string): void }).__novalistLayout(mode)
          }, layout)
          break
      }
    } catch (ex) { error = String(ex) }
    await page.evaluate((data) => {
      (window as unknown as { __novalistHostResult(data: string): void }).__novalistHostResult(data)
    }, Buffer.from(JSON.stringify({ id: call.id, ok: !error, result, error })).toString('base64'))
  })
  const address = server.address() as { port: number }
  await page.goto(`http://127.0.0.1:${address.port}/index.mobile.html`)
  await page.waitForFunction(() => !!window.novalistStores?.shell.getState().backendVersion)
  return {
    root,
    host,
    async close() {
      await page.close()
      backend.kill()
      await backendClosed
      server.closeAllConnections()
      await new Promise<void>((done) => server.close(() => done()))
      rmSync(root, { force: true, recursive: true })
    }
  }
}
