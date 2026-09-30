import { expect, test } from '@playwright/test'
import { copyFileSync, cpSync, mkdirSync, mkdtempSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { launchApp } from './harness'

test('real AI Assistant exposes local dictation first, independently of chat configuration', async () => {
  const build = process.env.NOVALIST_AIASSISTANT_BUILD ?? resolve('../../novalist-aiassistant/bin/Debug/net8.0')
  const settings = mkdtempSync(join(tmpdir(), 'nl-dictation-settings-'))
  const extension = join(settings, 'Extensions', 'AiAssistant')
  mkdirSync(extension, { recursive: true })
  for (const name of ['Novalist.Extensions.AiAssistant.dll', 'extension.json']) {
    copyFileSync(join(build, name), join(extension, name))
  }
  cpSync(join(build, 'Locales'), join(extension, 'Locales'), { recursive: true })
  const h = await launchApp('nl-dictation-settings-ui-', {
    NOVALIST_SETTINGS_DIR: settings,
    NOVALIST_BACKEND_PATH: resolve('../Novalist.Backend/bin/Debug/net8.0/Novalist.Backend.exe')
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
