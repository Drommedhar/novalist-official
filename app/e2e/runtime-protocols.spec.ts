import { test, expect } from '@playwright/test'
import { mkdirSync, writeFileSync } from 'node:fs'
import { createServer } from 'node:http'
import type { AddressInfo } from 'node:net'
import { join } from 'node:path'
import { launchApp } from './harness'

// Exercise the installed Electron runtime: browser-only tests do not register
// these schemes or enforce Electron's custom-protocol security rules.
test('renderer plugins can import modules from their registered extension', async () => {
  const h = await launchApp('nl-runtime-plugin-')
  try {
    const root = join(h.workDir, 'extension')
    mkdirSync(root)
    writeFileSync(join(root, 'value.mjs'), 'export const value = 42;')
    writeFileSync(join(root, 'plugin.mjs'), "export { value } from './value.mjs';")
    await h.page.evaluate((path) => window.novalist.registerExtensionRoots({ runtime: path }), root)
    const value = await h.page.evaluate(async () => {
      const url = 'novalist-ext://runtime/plugin.mjs'
      return (await import(url)).value
    })
    expect(value).toBe(42)
  } finally {
    await h.close()
  }
})

test('sandboxed extension panels can read their own files but not other local resources', async () => {
  const h = await launchApp('nl-runtime-panel-')
  try {
    const root = join(h.workDir, 'extension')
    const other = join(h.workDir, 'other-extension')
    const cache = join(h.workDir, 'settings', 'narration-cache')
    mkdirSync(root)
    mkdirSync(other)
    mkdirSync(cache, { recursive: true })
    writeFileSync(join(root, 'own.txt'), 'own data')
    writeFileSync(join(other, 'private.txt'), 'other extension data')
    writeFileSync(join(cache, 'abc123.wav'), 'narration data')
    writeFileSync(join(root, 'panel.html'), `<!doctype html><body><script>
      Promise.all([
        './own.txt',
        'novalist-ext://other/private.txt',
        'novalist-audio://clip/abc123.wav'
      ].map(async url => {
        try { const response = await fetch(url); return await response.text(); }
        catch { return 'blocked'; }
      })).then(results => { document.body.textContent = JSON.stringify(results); });
    </script></body>`)
    await h.page.evaluate(async (roots) => {
      await window.novalist.registerExtensionRoots(roots)
      const panel = document.createElement('iframe')
      panel.id = 'runtime-panel'
      panel.sandbox.add('allow-scripts')
      panel.src = 'novalist-ext://runtime/panel.html'
      document.body.appendChild(panel)
    }, { runtime: root, other })
    await expect(h.page.frameLocator('#runtime-panel').locator('body'))
      .toHaveText(JSON.stringify(['own data', 'blocked', 'blocked']))
  } finally {
    await h.close()
  }
})

test('web pages and opaque origins cannot read local extension or narration files', async () => {
  const h = await launchApp('nl-runtime-cors-')
  const server = createServer((_request, response) => {
    response.setHeader('Content-Type', 'text/html')
    response.end('<!doctype html><title>Untrusted origin</title>')
  })
  try {
    const root = join(h.workDir, 'extension')
    const cache = join(h.workDir, 'settings', 'narration-cache')
    mkdirSync(root)
    mkdirSync(cache, { recursive: true })
    writeFileSync(join(root, 'private.txt'), 'extension data')
    writeFileSync(join(cache, 'abc123.wav'), 'narration data')
    await h.page.evaluate((path) => window.novalist.registerExtensionRoots({ runtime: path }), root)
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
    const port = (server.address() as AddressInfo).port
    for (const origin of [`http://127.0.0.1:${port}/`, 'data:text/html,Opaque%20origin']) {
      const readable = await h.app.evaluate(async ({ BrowserWindow }, url) => {
        const window = new BrowserWindow({
          show: false,
          webPreferences: { sandbox: true, contextIsolation: true, nodeIntegration: false }
        })
        try {
          await window.loadURL(url)
          return await window.webContents.executeJavaScript(`
            Promise.all([
              'novalist-ext://runtime/private.txt',
              'novalist-audio://clip/abc123.wav'
            ].map(async url => {
              try { const response = await fetch(url); return await response.text(); }
              catch { return 'blocked'; }
            }))
          `)
        } finally {
          window.destroy()
        }
      }, origin)
      expect(readable, origin).toEqual(['blocked', 'blocked'])
    }
  } finally {
    await new Promise<void>((resolve) => server.close(() => resolve()))
    await h.close()
  }
})

test('project images and narration clips load through their local protocols', async () => {
  const h = await launchApp('nl-runtime-assets-')
  try {
    const root = join(h.workDir, 'project')
    const cache = join(h.workDir, 'settings', 'narration-cache')
    mkdirSync(root)
    mkdirSync(cache, { recursive: true })
    const png = Buffer.from(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
      'base64'
    )
    writeFileSync(join(root, 'image.png'), png)
    // A short silent PCM WAV is sufficient to verify media decoding as well as
    // fetch access, without using a speech engine or the system audio output.
    const wav = Buffer.alloc(44 + 1600)
    wav.write('RIFF', 0)
    wav.writeUInt32LE(wav.length - 8, 4)
    wav.write('WAVEfmt ', 8)
    wav.writeUInt32LE(16, 16)
    wav.writeUInt16LE(1, 20)
    wav.writeUInt16LE(1, 22)
    wav.writeUInt32LE(8000, 24)
    wav.writeUInt32LE(16000, 28)
    wav.writeUInt16LE(2, 32)
    wav.writeUInt16LE(16, 34)
    wav.write('data', 36)
    wav.writeUInt32LE(1600, 40)
    writeFileSync(join(cache, 'abc123.wav'), wav)
    await h.page.evaluate((path) => window.novalist.setProjectRoot(path), root)
    const imageWidth = await h.page.evaluate(async () => {
      const image = new Image()
      image.src = 'novalist-project://nl/image.png'
      await image.decode()
      return image.naturalWidth
    })
    expect(imageWidth).toBe(1)
    const audio = await h.page.evaluate(async () => {
      const url = 'novalist-audio://clip/abc123.wav'
      const response = await fetch(url)
      const bytes = (await response.arrayBuffer()).byteLength
      const media = new Audio()
      const loaded = new Promise<void>((resolve, reject) => {
        media.onloadedmetadata = () => resolve()
        media.onerror = () => reject(new Error(media.error?.message ?? 'Audio failed to load'))
      })
      media.src = url
      await loaded
      return { bytes, duration: media.duration }
    })
    expect(audio.bytes).toBe(wav.length)
    expect(audio.duration).toBeCloseTo(0.1, 2)
  } finally {
    await h.close()
  }
})
