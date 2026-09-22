import { expect, test } from '@playwright/test'
import { copyFileSync, cpSync, existsSync, mkdirSync, mkdtempSync } from 'node:fs'
import { createServer } from 'node:http'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { launchApp, seedBook } from './harness'

test('AI services are providers, update their address, and discover models at the edited endpoint', async () => {
  const build = process.env.NOVALIST_AIASSISTANT_BUILD ?? resolve('..', '..', 'novalist-aiassistant', 'bin', 'Debug', 'net8.0')
  test.skip(!existsSync(join(build, 'Novalist.Extensions.AiAssistant.dll')), 'AI Assistant extension must be built')
  const settings = mkdtempSync(join(tmpdir(), 'nl-ai-provider-settings-'))
  const extension = join(settings, 'Extensions', 'AiAssistant')
  mkdirSync(extension, { recursive: true })
  for (const name of ['Novalist.Extensions.AiAssistant.dll', 'extension.json']) {
    copyFileSync(join(build, name), join(extension, name))
  }
  cpSync(join(build, 'Locales'), join(extension, 'Locales'), { recursive: true })

  const paths: string[] = []
  const server = createServer((request, response) => {
    paths.push(request.url ?? '')
    response.writeHead(request.url === '/v1/models' ? 200 : 404, { 'Content-Type': 'application/json' })
    response.end(JSON.stringify({ data: [{ id: 'ollama-test-model' }] }))
  })
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
  const address = server.address() as { port: number }
  const url = `http://127.0.0.1:${address.port}/v1`
  const h = await launchApp('nl-ai-provider-ui-', { NOVALIST_SETTINGS_DIR: settings })
  try {
    expect(await h.rpc('extensions/load')).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: 'com.novalist.ai', loadError: null })
    ]))
    await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('extensions'))
    const form = h.page.locator('.ext-schema-form').filter({ hasText: 'AI / LLM' })
    const provider = form.getByRole('combobox', { name: /^Provider/ })
    const baseUrl = form.getByRole('textbox', { name: 'Base URL', exact: true })
    await expect(provider.locator('option:checked')).toHaveText('LM Studio')
    await expect(form.getByText('Endpoint preset', { exact: true })).toHaveCount(0)
    await provider.selectOption({ label: 'Ollama' })
    await expect(baseUrl).toHaveValue('http://localhost:11434/v1')

    await baseUrl.fill(url)
    // Clicking immediately also exercises flushing the new address before the
    // model request, instead of querying the previous persisted connection.
    await form.getByRole('button', { name: 'Refresh', exact: true }).click()
    await expect(form.locator('datalist option[value="ollama-test-model"]')).toHaveCount(1)
    expect(paths).toEqual(['/v1/models'])
    await expect(baseUrl).toHaveValue(url)

    // A slow provider save must not overwrite a URL typed while it was pending.
    const pending = await h.page.evaluateHandle(() => {
      const rpc = window.novalistRpc
      const original = rpc.request.bind(rpc)
      let release!: () => void
      const gate = new Promise<void>((resolve) => { release = resolve })
      const state = { started: false, release }
      rpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
        if (method === 'extensions/settingsSchema/save' && !state.started) {
          state.started = true
          await gate
        }
        return original<T>(method, params)
      }
      return state
    })
    await provider.selectOption({ label: 'OpenAI' })
    await expect.poll(() => pending.evaluate((state) => state.started)).toBe(true)
    await baseUrl.fill('http://my-proxy.invalid/v1')
    await pending.evaluate((state) => state.release())
    await expect.poll(async () => {
      const schemas = await h.rpc<{ fields: { key: string; value: string }[] }[]>('extensions/settingsSchema')
      return schemas[0]?.fields.find((field) => field.key === 'lmStudioBaseUrl')?.value
    }).toBe('http://my-proxy.invalid/v1')
    await expect(baseUrl).toHaveValue('http://my-proxy.invalid/v1')

    await provider.selectOption({ label: 'Anthropic API' })
    await expect(baseUrl).toHaveCount(0)
    await expect(form.getByRole('textbox', { name: 'Anthropic API address', exact: true })).toBeVisible()
    await provider.selectOption({ label: 'Ollama' })
    await expect(baseUrl).toHaveValue('http://localhost:11434/v1')

    // Exercise the extension's real first-run wizard, including its model
    // validator and dynamic choices, rather than a hand-built definition.
    await h.page.evaluate(() => window.novalistStores.onboarding.getState().skipTour())
    await seedBook(h, {})
    const wizard = h.page.getByRole('dialog', { name: 'AI Assistant — setup' })
    await wizard.getByRole('radio', { name: 'Yes — set it up now', exact: true }).check()
    await wizard.locator('.dialog-button.primary').click()
    await expect(wizard.getByRole('radio', { name: 'Ollama', exact: true })).toBeChecked()
    await wizard.locator('.dialog-button.primary').click()
    await expect(wizard.locator('.wizard-host-label')).toHaveText('Base URL')
    await wizard.getByRole('textbox').fill(url)
    await wizard.locator('.dialog-button.primary').click()
    await expect(wizard.locator('.wizard-host-label')).toHaveText('API Token')
    await wizard.locator('.dialog-button.primary').click()
    await wizard.getByRole('radio', { name: 'ollama-test-model', exact: true }).check()
    await wizard.locator('.dialog-button.primary').click()
    await expect(wizard.locator('.wizard-host-label')).toHaveText('Response language')
    await wizard.locator('.dialog-button.primary').click()
    await expect(wizard).toHaveCount(0)
    await expect.poll(async () => {
      const schemas = await h.rpc<{ fields: { key: string; value: string }[] }[]>('extensions/settingsSchema')
      return Object.fromEntries(schemas[0].fields.map((field) => [field.key, field.value]))
    }).toMatchObject({ enabled: 'true', provider: 'ollama', lmStudioBaseUrl: url, lmStudioModel: 'ollama-test-model' })
    expect(paths).toEqual(['/v1/models', '/v1/models', '/v1/models'])
  } finally {
    await h.close()
    await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve()))
  }
})
