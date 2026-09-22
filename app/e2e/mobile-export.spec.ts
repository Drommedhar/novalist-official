import { test, expect, type Page } from '@playwright/test'
import { spawn } from 'node:child_process'
import { createServer } from 'node:http'
import { mkdtempSync, mkdirSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { basename, extname, join, resolve, sep } from 'node:path'
import { dismissTour, type Book } from './harness'

/** The actual mobile bundle and HybridWebView shim, connected to the real
 * backend. Only UIKit's file chooser/share sheet is represented by a fake. */
async function mobileHost(page: Page, layout: 'phone' | 'tablet') {
  const root = mkdtempSync(join(tmpdir(), 'nl-mobile-export-'))
  const bundle = resolve('../Novalist.Mobile/Resources/Raw/app')
  const backend = spawn('dotnet', [resolve('../Novalist.Backend/bin/Debug/net8.0/Novalist.Backend.dll')], {
    env: { ...process.env, NOVALIST_SETTINGS_DIR: join(root, 'settings') },
    stdio: ['pipe', 'pipe', 'pipe']
  })
  backend.stderr.resume()
  let deliveries = Promise.resolve()
  backend.stdout.on('data', (bytes: Buffer) => {
    deliveries = deliveries.then(() => page.evaluate((data) => {
      (window as unknown as { __novalistRecv(data: string): void }).__novalistRecv(data)
    }, bytes.toString('base64'))).catch(() => {})
  })
  const host = {
    root,
    outcome: 'success' as 'success' | 'cancel' | 'error',
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
          result = host.outcome === 'success'
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
      server.closeAllConnections()
      await new Promise<void>((done) => server.close(() => done()))
      rmSync(root, { force: true, recursive: true })
    }
  }
}

async function seed(page: Page, root: string): Promise<Book> {
  const book = await page.evaluate(async (directory) => {
    const rpc = window.novalistRpc
    await rpc.request('project/create', [directory, 'Export test', 'Book'])
    for (const title of ['Keep', 'Leave out']) {
      const s = await rpc.request<Book>('project/createChapter', [title])
      const chapter = s.chapters.at(-1)!
      const withScene = await rpc.request<Book>('project/createScene', [chapter.guid, title])
      const scene = withScene.chapters.at(-1)!.scenes[0]
      await rpc.request('scenes/write', [chapter.guid, scene.id,
        `<p>${title === 'Keep' ? 'KEPT_PROSE' : 'OMITTED_PROSE'}</p>`, title])
    }
    const state = await rpc.request<Book>('project/getState')
    window.novalistStores.project.getState().applyState(state as never)
    return state
  }, root)
  await dismissTour(page)
  await page.evaluate(() => {
    (window as unknown as { __novalistTab(key: string): void }).__novalistTab(
      window.novalistStores.shell.getState().mobileLayout === 'tablet' ? 'write' : 'manuscript')
  })
  await expect(page.locator('.binder-chapter-title').first()).toHaveText('Keep')
  return book
}

for (const layout of ['phone', 'tablet'] as const) {
  test(`mobile export: ${layout} chapter selection reaches the native share boundary`, async ({ page }) => {
    await page.setViewportSize(layout === 'phone' ? { width: 393, height: 852 } : { width: 1180, height: 820 })
    const h = await mobileHost(page, layout)
    try {
      await seed(page, h.root)
      await page.locator('.binder-chapter').first().getByRole('button', { name: 'Chapters', exact: true }).click()
      await page.getByRole('menuitem', { name: 'Export chapter…' }).click()
      await expect(page.locator('.export-view')).toBeVisible()
      await expect(page.locator('.export-chapters input:checked')).toHaveCount(1)
      await expect(page.locator('.export-chapters').getByLabel('Keep', { exact: true })).toBeChecked()
      if (layout === 'tablet') {
        // Narrow Split View remounts the phone shell. It must not silently turn
        // a one-chapter export into an export of the entire book.
        for (const mode of ['phone', 'tablet']) {
          await page.setViewportSize(mode === 'phone' ? { width: 500, height: 820 } : { width: 1180, height: 820 })
          await page.evaluate((value) => {
            (window as unknown as { __novalistLayout(mode: string): void }).__novalistLayout(value)
          }, mode)
          await expect(page.locator(mode === 'phone' ? '.mobile-shell' : '.tablet-shell')).toBeVisible()
          await expect(page.locator('.export-chapters input:checked')).toHaveCount(1)
        }
      }
      await page.selectOption('#export-format', 'Markdown')
      await page.locator('.export-run').click()
      await expect(page.locator('.export-result')).toHaveText('Export complete.')
      expect(h.host.shared).toHaveLength(1)
      expect(h.host.shared[0].bytes.toString()).toContain('KEPT_PROSE')
      expect(h.host.shared[0].bytes.toString()).not.toContain('OMITTED_PROSE')
      await expect.poll(() => h.host.released).toEqual([h.host.shared[0].path])
      expect(await page.locator('.export-view').evaluate((el) => el.scrollWidth <= el.clientWidth + 1)).toBe(true)

      // Return via the real native navigation callback; the Write tab must work
      // even though it remained highlighted while Export was open.
      await page.evaluate((mode) => {
        (window as unknown as { __novalistTab(key: string): void }).__novalistTab(mode === 'phone' ? 'manuscript' : 'write')
      }, layout)
      await page.locator('.binder-mobile-actions').getByRole('button', { name: 'Export', exact: true }).click()
      await expect(page.locator('.export-chapters input:checked')).toHaveCount(2)
    } finally { await h.close() }
  })
}

test('mobile export: cancel, retry, and failures never report a successful save', async ({ page }) => {
  const h = await mobileHost(page, 'phone')
  try {
    await seed(page, h.root)
    await page.locator('.binder-mobile-actions').getByRole('button', { name: 'Export', exact: true }).click()
    await page.selectOption('#export-format', 'Markdown')
    h.host.outcome = 'cancel'
    await page.locator('.export-run').click()
    await expect(page.locator('.export-result')).toHaveText('Export cancelled.')
    await expect(page.locator('.export-run')).toBeEnabled()
    h.host.outcome = 'error'
    await page.locator('.export-run').click()
    await expect(page.locator('.export-result')).toHaveText('Export failed.')
    await expect(page.locator('.export-run')).toBeEnabled()
    h.host.outcome = 'success'
    await page.locator('.export-run').click()
    await expect(page.locator('.export-result')).toHaveText('Export complete.')
    await expect.poll(() => h.host.released.length).toBe(3)
    h.host.failSave = true
    await page.locator('.export-run').click()
    await expect(page.locator('.export-result')).toHaveText('Export failed.')
    await expect(page.locator('.export-run')).toBeEnabled()
    expect(h.host.shared).toHaveLength(3)
    h.host.failSave = false
    h.host.failExport = true
    await page.locator('.export-run').click()
    await expect(page.locator('.export-result')).toHaveText('Export failed.')
    await expect(page.locator('.export-run')).toBeEnabled()
    await expect.poll(() => h.host.released.length).toBe(4)
    expect(h.host.shared).toHaveLength(3)
  } finally { await h.close() }
})
