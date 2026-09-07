import { expect, test } from '@playwright/test'
import { createServer } from 'node:http'
import { dismissTour, enterWriting, launchApp, seedBook, type Harness } from './harness'

async function grammarServer() {
  const requests: string[] = []
  const server = createServer(async (request, response) => {
    const chunks: Buffer[] = []
    for await (const chunk of request) chunks.push(Buffer.from(chunk))
    const text = new URLSearchParams(Buffer.concat(chunks).toString()).get('text') ?? ''
    requests.push(text)
    // Deliberately ignores the local dictionary, as the free endpoint does.
    const matches = [...text.matchAll(/\b(Aelthorn|wrold)\b/g)].map(match => ({
      message: 'Possible spelling mistake', offset: match.index, length: match[0].length,
      rule: { category: { id: 'TYPOS' } }, replacements: [{ value: 'world' }]
    }))
    response.writeHead(200, { 'Content-Type': 'application/json' })
    response.end(JSON.stringify({ matches }))
  })
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve))
  return {
    url: `http://127.0.0.1:${(server.address() as { port: number }).port}/v2/check`,
    requests,
    close: () => new Promise<void>((resolve, reject) =>
      server.close(error => error ? reject(error) : resolve()))
  }
}

async function reopenEditor(h: Harness): Promise<void> {
  await h.page.reload()
  await h.page.waitForFunction(() => !!window.novalistStores?.settings.getState().view)
  await enterWriting(h.page)
  await h.page.locator('.binder-scene-row').first().click()
  await expect(h.page.frameLocator('.editor-frame').locator('#editor')).toBeVisible()
}

test('Harper checks British English offline and keeps learned words after reopening', async () => {
  const h = await launchApp('nl-harper-')
  const server = await grammarServer()
  try {
    await dismissTour(h.page)
    await h.page.evaluate(async url => {
      await window.novalistStores.settings.getState().update('global', {
        grammarCheckProvider: 'harper', grammarCheckEnabled: true,
        autoReplacementLanguage: 'en', autoReplacementEnabled: false,
        spellCheckLanguages: ['en-GB', 'en-GB-oxendict'], spellCheckEnabled: true,
        grammarCheckApiUrl: url, grammarCheckUsername: 'saved-user', grammarCheckApiKey: 'saved-key'
      })
    }, server.url)
    // Before the lazy engine has loaded: first use cannot rely on a CDN/cache.
    await h.page.context().setOffline(true)
    const book = await seedBook(h, { One: ['Scene'] })
    await h.rpc('entities/create', ['character', 'Nemorra'])
    await h.page.locator('.binder-scene-row').first().click()
    const frame = h.page.frameLocator('.editor-frame')
    const editor = frame.locator('#editor')
    await expect(editor).toBeVisible()
    const text = String.fromCodePoint(0x1D11E) + ' This is a example. I realise it now. Aelthorn lives here. Nemorra lives here.'
    await editor.fill(text)
    // An astral character precedes the finding; the replacement must still
    // land on the article, using the editor's UTF-16 offsets.
    await expect(frame.locator('.grammar-issue', { hasText: /^a$/ })).toHaveCount(1, { timeout: 30_000 })
    await expect(frame.locator('.grammar-issue', { hasText: 'realise' })).toHaveCount(0)
    await expect(frame.locator('.grammar-issue', { hasText: 'Nemorra' })).toHaveCount(0)
    await frame.locator('.grammar-issue', { hasText: /^a$/ }).click()
    await frame.locator('.gp-suggestion', { hasText: /^an$/ }).click()
    await expect(editor).toContainText('This is an example.')

    await frame.locator('.grammar-issue', { hasText: 'Aelthorn' }).click()
    await frame.locator('.gp-suggestion', { hasText: 'Add to Dictionary' }).click()
    await expect.poll(() => h.rpc<string[]>('spell/words')).toEqual(['Aelthorn'])
    await expect(frame.locator('.grammar-issue')).toHaveCount(0, { timeout: 20_000 })
    if (process.platform !== 'darwin') {
      expect(await h.app.evaluate(({ session }) => session.defaultSession.listWordsInSpellCheckerDictionary()))
        .toContain('Aelthorn')
    }

    // A fresh check and a fresh renderer must still accept the learned name.
    // Keep one real error so an idle/broken checker cannot satisfy the test.
    await editor.fill(text)
    await expect(frame.locator('.grammar-issue')).toHaveCount(1, { timeout: 20_000 })
    await expect(frame.locator('.grammar-issue')).toHaveText('a')
    await expect.poll(async () => (await h.rpc<{ html: string }>('scenes/read', [
      book.chapters[0].guid, book.chapters[0].scenes[0].id
    ])).html).toContain('This is a example.')
    await reopenEditor(h)
    await expect(frame.locator('.grammar-issue')).toHaveCount(1, { timeout: 30_000 })
    await expect(frame.locator('.grammar-issue')).toHaveText('a')

    // Changing dialect rebuilds Harper's internal dictionary. Learned words
    // and Codex names must be re-imported even though the saved list is unchanged.
    await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', {
      spellCheckLanguages: ['en-US']
    }))
    await expect(frame.locator('.grammar-issue')).toHaveCount(2, { timeout: 20_000 })
    await expect(frame.locator('.grammar-issue', { hasText: 'realise' })).toHaveCount(1)
    await expect(frame.locator('.grammar-issue', { hasText: /Aelthorn|Nemorra/ })).toHaveCount(0)
    await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', {
      spellCheckLanguages: ['en-GB']
    }))
    await expect(frame.locator('.grammar-issue')).toHaveCount(1, { timeout: 20_000 })
    await expect(frame.locator('.grammar-issue')).toHaveText('a')

    // Unsupported writing languages never silently fall back to a server.
    await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', {
      autoReplacementLanguage: 'de-low'
    }))
    await expect(frame.locator('.grammar-issue')).toHaveCount(0, { timeout: 20_000 })
    expect(server.requests).toEqual([])
  } finally {
    await h.close()
    await server.close()
  }
})

test('the grammar provider control follows global and project scope', async () => {
  const h = await launchApp('nl-grammar-provider-')
  try {
    await dismissTour(h.page)
    await seedBook(h, {})
    await h.page.evaluate(() => window.novalistStores.shell.getState().openSettings('writingAssistance'))
    const provider = h.page.locator('#set-gc-provider')
    await expect(provider).toHaveValue('languagetool')
    await provider.selectOption('harper')
    await expect(provider).toHaveValue('harper')
    await expect(h.page.locator('#set-gc-url')).toHaveCount(0)
    await expect(h.page.getByText('Free English grammar, spelling, and style checks on your device.', { exact: false })).toBeVisible()

    const scope = h.page.locator('.settings-scope input[type="checkbox"]')
    await scope.click()
    await expect(scope).toBeChecked()
    await provider.selectOption('languagetool')
    await expect(h.page.locator('#set-gc-url')).toBeVisible()
    const view = await h.rpc<{ global: { grammarCheckProvider: string }, overrides: { grammarCheckProvider: string } }>('settings/get')
    expect(view.global.grammarCheckProvider).toBe('harper')
    expect(view.overrides.grammarCheckProvider).toBe('languagetool')
    await scope.click()
    await expect(scope).not.toBeChecked()
    await expect(provider).toHaveValue('harper')
    await h.page.evaluate(() => window.novalistStores.settings.getState().update('global', {
      autoReplacementLanguage: 'de-low'
    }))
    await expect(h.page.getByText('Harper checks English only.', { exact: false })).toBeVisible()
  } finally {
    await h.close()
  }
})

test('learning a word clears LanguageTool spelling warnings without a paid account', async () => {
  const h = await launchApp('nl-lt-dictionary-')
  const server = await grammarServer()
  try {
    await dismissTour(h.page)
    await h.page.evaluate(url => window.novalistStores.settings.getState().update('global', {
      grammarCheckProvider: 'languagetool', grammarCheckEnabled: true,
      grammarCheckApiUrl: url, grammarCheckApiKey: null, grammarCheckUsername: null
    }), server.url)
    await seedBook(h, { One: ['Scene'] })
    await h.page.locator('.binder-scene-row').first().click()
    const frame = h.page.frameLocator('.editor-frame')
    await expect(frame.locator('#editor')).toBeVisible()
    await frame.locator('#editor').fill('Aelthorn sees the wrold.')
    await expect(frame.locator('.grammar-issue')).toHaveCount(2, { timeout: 20_000 })
    await frame.locator('.grammar-issue', { hasText: 'Aelthorn' }).click()
    await frame.locator('.gp-suggestion', { hasText: 'Add to Dictionary' }).click()
    await expect(frame.locator('.grammar-issue')).toHaveCount(1, { timeout: 20_000 })
    await expect(frame.locator('.grammar-issue')).toHaveText('wrold')
    await reopenEditor(h)
    await expect(frame.locator('.grammar-issue')).toHaveCount(1, { timeout: 20_000 })
    await expect(frame.locator('.grammar-issue')).toHaveText('wrold')
    expect(server.requests.length).toBeGreaterThanOrEqual(3)
    expect(await h.rpc<string[]>('spell/words')).toEqual(['Aelthorn'])
  } finally {
    await h.close()
    await server.close()
  }
})
