import { expect, test } from '@playwright/test'
import { copyFileSync, cpSync, existsSync, mkdirSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { launchApp } from './harness'

test('system language preparation is available without any extension', async () => {
  const h = await launchApp('nl-system-dictation-settings-', {
    NOVALIST_BACKEND_PATH: resolve('../Novalist.Backend/bin/Debug/net8.0/Novalist.Backend' + (process.platform === 'win32' ? '.exe' : ''))
  })
  try {
    await h.page.evaluate(() => {
      let installed = false
      const original = window.novalistRpc.request.bind(window.novalistRpc)
      window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
        if (method === 'dictation/systemStatus') return { engine: 'apple', available: true, online: false,
          usesSystemPanel: false, languages: [{ language: 'en', supported: true, installed: true },
            { language: 'de', supported: true, installed }] } as T
        if (method === 'dictation/prepareSystem') {
          const args = params as { requestId: string; language: string }
          if (args.language !== 'de' || !args.requestId) throw new Error('Invalid preparation request')
          installed = true
          return undefined as T
        }
        return original<T>(method, params)
      }
      window.novalistStores.shell.getState().openSettings('writingAssistance')
    })
    const card = h.page.locator('#set-system-dictation')
    const prepare = card.getByRole('button', { name: 'Prepare system language', exact: true })
    await expect(prepare).toBeDisabled()
    await card.getByRole('combobox').selectOption('de')
    await expect(prepare).toBeEnabled()
    await prepare.click()
    await expect(card).toContainText('This language is ready for on-device dictation.')
    await expect(prepare).toBeDisabled()
    await expect(card.getByRole('alert')).toHaveCount(0)
  } finally { await h.close() }
})

test('real AI Assistant exposes local dictation first, independently of chat configuration', async () => {
  const build = process.env.NOVALIST_AIASSISTANT_BUILD ?? resolve('../../novalist-aiassistant/bin/Debug/net8.0')
  test.skip(!existsSync(join(build, 'Novalist.Extensions.AiAssistant.dll')), 'Requires the separately built AI Assistant repository')
  const settings = mkdtempSync(join(tmpdir(), 'nl-dictation-settings-'))
  const extension = join(settings, 'Extensions', 'AiAssistant')
  mkdirSync(extension, { recursive: true })
  for (const name of ['Novalist.Extensions.AiAssistant.dll', 'extension.json']) {
    copyFileSync(join(build, name), join(extension, name))
  }
  cpSync(join(build, 'Locales'), join(extension, 'Locales'), { recursive: true })
  const h = await launchApp('nl-dictation-settings-ui-', {
    NOVALIST_SETTINGS_DIR: settings,
    NOVALIST_BACKEND_PATH: resolve('../Novalist.Backend/bin/Debug/net8.0/Novalist.Backend' + (process.platform === 'win32' ? '.exe' : ''))
  })
  try {
    expect(await h.rpc('extensions/load')).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: 'com.novalist.ai', loadError: null })
    ]))
    await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('extensions'))
    const form = h.page.locator('.ext-schema-form').filter({ hasText: 'AI / LLM' })
    await expect(form.locator('.ext-schema-group').first()).toHaveText('Dictation')
    await expect(form.getByRole('checkbox', { name: /^Enable local dictation/ })).toBeChecked()
    await expect(form.getByRole('checkbox', { name: /^Enable AI features/ })).not.toBeChecked()
    await expect(form.getByRole('button', { name: 'Download / repair dictation models' })).toBeVisible()
    await expect(form.getByText(/Download the selected models before/)).toBeVisible()
    const acceleration = form.getByRole('combobox', { name: /^Acceleration/ })
    await expect(acceleration).toHaveValue('auto')
    await expect(acceleration.locator('option')).toHaveText([
      /Automatic \((CPU|CUDA|ROCM|MLX)\)/, 'CPU', 'NVIDIA CUDA', 'AMD ROCm', 'Apple Silicon (MLX)'
    ])
    await acceleration.selectOption('rocm')
    const speech = form.getByRole('combobox', { name: 'Speech recognition model', exact: true })
    await expect(speech).toHaveValue('small')
    await expect(speech.locator('option[value="large-v3"]')).toHaveText('Whisper Large v3 (~3.1 GB)')
    await speech.selectOption('large-v3')
    await expect.poll(async () => {
      const schemas = await h.rpc<{ extensionId: string; fields: { key: string; value: string }[] }[]>('extensions/settingsSchema')
      return schemas.find((s) => s.extensionId === 'com.novalist.ai')?.fields.find((f) => f.key === 'dictationModel')?.value
    }).toBe('large-v3')
    const schemas = await h.rpc<{ fields: { key: string }[] }[]>('extensions/settingsSchema')
    const keys = schemas.flatMap((s) => s.fields.map((f) => f.key))
    expect(keys).not.toContain('dictationEndpoint')
    expect(keys).not.toContain('dictationApiKey')
    expect(await h.rpc('dictation/providers')).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: 'com.novalist.ai', available: false, audioDestination: 'Whisper large-v3' })
    ]))
    // The model setting persists after leaving and reopening the settings UI.
    await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('appearance'))
    await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('extensions'))
    await expect(speech).toHaveValue('large-v3')
    await expect(acceleration).toHaveValue('rocm')
  } finally { await h.close() }
})
