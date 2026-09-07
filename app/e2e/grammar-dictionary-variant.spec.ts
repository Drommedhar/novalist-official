import { expect, test } from '@playwright/test'
import { createServer } from 'node:http'
import { dismissTour, launchApp, seedBook } from './harness'

test('British spelling dictionaries also select British English for grammar checking', async () => {
  const languages: string[] = []
  const server = createServer(async (request, response) => {
    const chunks: Buffer[] = []
    for await (const chunk of request) chunks.push(Buffer.from(chunk))
    languages.push(new URLSearchParams(Buffer.concat(chunks).toString()).get('language') ?? '')
    response.writeHead(200, { 'Content-Type': 'application/json' })
    response.end('{"matches": []}')
  })
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
  const address = server.address() as { port: number }
  const h = await launchApp('novalist-grammar-variant-')
  try {
    await seedBook(h, {})
    await dismissTour(h.page)
    await h.page.evaluate(async (url) => {
      await window.novalistStores.settings.getState().update('global', {
        autoReplacementLanguage: 'en', spellCheckLanguages: ['en'],
        spellCheckEnabled: true, grammarCheckEnabled: true, grammarCheckApiUrl: url
      })
      // The operating system's available dictionaries differ by platform. The
      // settings and grammar calls remain real; only that platform list is fixed.
      window.novalist.spellCheckLanguages = async () => ['en', 'en-GB', 'en-GB-oxendict', 'en-US']
      window.novalistStores.shell.getState().openSettings('writingAssistance')
    }, `http://127.0.0.1:${address.port}/v2/check`)
    for (const tag of ['en-GB', 'en-GB-oxendict']) {
      await h.page.getByRole('checkbox', { name: tag, exact: true }).click()
      await expect(h.page.getByRole('checkbox', { name: tag, exact: true })).toBeChecked()
      await expect.poll(async () => (await h.rpc<{ effective: { spellCheckLanguages: string[] } }>(
        'settings/get'
      )).effective.spellCheckLanguages).toContain(tag)
    }
    await h.page.getByRole('checkbox', { name: 'en', exact: true }).click()
    await expect(h.page.getByRole('checkbox', { name: 'en', exact: true })).not.toBeChecked()
    await expect.poll(async () => (await h.rpc<{ effective: { spellCheckLanguages: string[] } }>(
      'settings/get'
    )).effective.spellCheckLanguages).toEqual(['en-GB', 'en-GB-oxendict'])
    expect(await h.rpc('grammar/check', ['I realise it now.'])).toEqual([])
    expect(languages.at(-1)).toBe('en-GB')
    await h.page.getByRole('checkbox', { name: 'en-GB-oxendict', exact: true }).click()
    await expect(h.page.getByRole('checkbox', { name: 'en-GB-oxendict', exact: true })).not.toBeChecked()
    await h.page.getByRole('checkbox', { name: 'en-US', exact: true }).click()
    await expect(h.page.getByRole('checkbox', { name: 'en-US', exact: true })).toBeChecked()
    await h.page.getByRole('checkbox', { name: 'en-GB', exact: true }).click()
    await expect(h.page.getByRole('checkbox', { name: 'en-GB', exact: true })).not.toBeChecked()
    await expect.poll(async () => (await h.rpc<{ effective: { spellCheckLanguages: string[] } }>(
      'settings/get'
    )).effective.spellCheckLanguages).toEqual(['en-US'])
    await h.rpc('grammar/check', ['I realize it now.'])
    expect(languages.at(-1)).toBe('en-US')
  } finally {
    await h.close()
    await new Promise<void>((resolve, reject) => server.close((error) => error ? reject(error) : resolve()))
  }
})
