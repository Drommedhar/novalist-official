import { test, expect, _electron as electron } from '@playwright/test'
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

/**
 * An extension panel wears the interface theme.
 *
 * A panel is an iframe, and a CSS custom property does not cross a document
 * boundary - so `:root { --nl-* }` in the shell was simply absent inside every
 * extension, and each one shipped its own palette instead. The AI panels were
 * VS Code grey inside a Novalist that is Ink Night and gilt, and picking
 * Discord or high-contrast changed everything except them.
 *
 * This asserts the whole route rather than the message: the shim is served by
 * the protocol handler for any extension, the panel actually resolves a token
 * to a real colour, the resolved value is the shell's own, and changing the
 * theme repaints the panel with the rest of the app.
 */
test('an extension panel resolves the design tokens and follows a theme change', async () => {
  test.setTimeout(120_000)

  const workDir = mkdtempSync(join(tmpdir(), 'nl-exttheme-'))
  const extensionDir = join(workDir, 'panel-extension')
  // Under web/, the way every real extension lays a panel out. At the root the
  // guide's relative <script src> resolves to /__novalist/theme.js and works
  // by accident; one folder down it resolves to /web/__novalist/theme.js, and
  // that is the path a real panel actually asks for.
  mkdirSync(join(extensionDir, 'web'), { recursive: true })

  // A panel written the way the extension guide says to write one: include the
  // shim, then style with the app's tokens and nothing else.
  writeFileSync(
    join(extensionDir, 'web', 'panel.html'),
    `<!doctype html><meta charset="utf-8">
     <script src="__novalist/theme.js"></script>
     <style>
       body { background: var(--nl-surface-card); color: var(--nl-text);
              font-family: var(--nl-font-family); font-size: var(--nl-font-ui); }
     </style>
     <body><p id="hello">Panel</p></body>`
  )

  const env: Record<string, string> = Object.fromEntries(
    Object.entries(process.env).filter(([k, v]) => v !== undefined && k !== 'ELECTRON_RUN_AS_NODE')
  ) as Record<string, string>
  env.NOVALIST_NO_SPLASH = '1'
  env.NOVALIST_SETTINGS_DIR = join(workDir, 'settings')

  const app = await electron.launch({ args: ['out/main/index.js'], env })
  const page = await app.firstWindow()
  await expect(page.locator('.status-backend.connected')).toBeVisible({ timeout: 30_000 })

  // Mount the panel the way ExtensionWebView does - same scheme, same sandbox,
  // same handshake - so what is under test is the real bridge and not a mock.
  await page.evaluate(async (root: string) => {
    await window.novalist.registerExtensionRoots({ paneltest: root })
    const { postThemeToFrame, watchTheme } = window.novalistExtensionTheme
    const frame = document.createElement('iframe')
    frame.id = 'theme-probe'
    frame.setAttribute('sandbox', 'allow-scripts')
    frame.src = 'novalist-ext://paneltest/web/panel.html'
    document.body.appendChild(frame)

    const send = (): void => postThemeToFrame(frame.contentWindow)
    window.addEventListener('message', (event) => {
      if (event.source === frame.contentWindow && event.data?.novalistThemeReady) send()
    })
    watchTheme(send)
  }, extensionDir)

  const panel = page.frameLocator('#theme-probe')
  // The shim reports when it has stamped the tokens on, so there is a state to
  // wait for rather than a sleep to guess at.
  await expect(panel.locator('html')).toHaveAttribute('data-novalist-theme', 'ready', {
    timeout: 15_000
  })

  const read = async (): Promise<{ background: string; token: string; font: string }> =>
    panel.locator('body').evaluate((body) => {
      const style = getComputedStyle(body)
      return {
        background: style.backgroundColor,
        token: getComputedStyle(document.documentElement).getPropertyValue('--nl-surface-card').trim(),
        font: style.fontSize
      }
    })

  const shellCard = await page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--nl-surface-card').trim()
  )

  const themed = await read()
  // A token that did not arrive resolves to nothing and the background falls
  // back to transparent, which is exactly the old behaviour.
  expect(themed.background).not.toBe('rgba(0, 0, 0, 0)')
  expect(themed.token).toBe(shellCard)
  // The type scale comes across too, not just the palette.
  expect(themed.font).toBe('15px')

  // Switching theme repaints the panel with the rest of the app.
  await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'catppuccin-mocha'))
  await expect
    .poll(async () => (await read()).token, { timeout: 15_000 })
    .toBe('#181825')

  const afterSwitch = await read()
  expect(afterSwitch.background).not.toBe(themed.background)

  await app.close()
})
