import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

test('narration shows the loaded book and cast when the frame becomes ready last', async () => {
  const h = await launchApp('nl-narration-ready-')
  try {
    const book = await seedBook(h, { Harbour: ['Arrival'], Kessel: ['Return'] })
    await h.rpc('entities/create', ['character', 'Mira'])
    for (const chapter of book.chapters) {
      await h.rpc('scenes/write', [
        chapter.guid, chapter.scenes[0].id,
        '<p>"You are late," Mira snapped.</p>', '"You are late," Mira snapped.'
      ])
    }
    await dismissTour(h.page)

    // Hold the real frame's handshake until the book and cast have rendered.
    // This forces the CI race without depending on machine speed or a sleep.
    const handshake = await h.page.evaluateHandle(() => {
      let received: MessageEvent | undefined
      let resolveReady: () => void = () => {}
      const ready = new Promise<void>((resolve) => { resolveReady = resolve })
      const holdReady = (event: MessageEvent): void => {
        const frame = document.querySelector<HTMLIFrameElement>('.narration-frame')
        if (!frame || event.source !== frame.contentWindow) return
        const raw = (event.data as { novalistNarration?: string })?.novalistNarration
        if (typeof raw !== 'string' || JSON.parse(raw).type !== 'ready') return
        event.stopImmediatePropagation()
        received = event
        resolveReady()
      }
      window.addEventListener('message', holdReady, true)
      return {
        ready,
        release: () => {
          if (!received) throw new Error('The narration frame has not announced readiness')
          window.removeEventListener('message', holdReady, true)
          window.dispatchEvent(new MessageEvent('message', {
            data: received.data, origin: received.origin, source: received.source
          }))
        }
      }
    })

    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('narration'))
    await handshake.evaluate(async (held) => { await held.ready })
    await expect.poll(() => h.page.evaluate(() => ({
      chapters: window.novalistStores.narration.getState().book?.chapters.map((c) => c.title),
      members: window.novalistStores.narration.getState().members.map((m) => m.name)
    }))).toEqual({ chapters: ['Harbour', 'Kessel'], members: ['Mira'] })
    const swatch = h.page.locator('.narration-cast-row:not(.narrator) .narration-cast-swatch')
    await expect(swatch).toHaveCount(1)
    // Flush the effects that tried to push the loaded data before readiness.
    await h.page.evaluate(() => new Promise<void>((resolve) => {
      requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
    }))
    const frame = h.page.frameLocator('.narration-frame')
    await expect(frame.locator('.nl-chapter')).toHaveCount(0)
    await handshake.evaluate((held) => held.release())
    await handshake.dispose()

    await expect(frame.locator('.nl-chapter-title')).toHaveText(['Harbour', 'Kessel'])
    const spoken = frame.locator('[data-nl-kind="dialogue"]')
    await expect(spoken).toHaveCount(2)
    await expect(spoken.first()).toHaveText('"You are late,"')
    const colour = await swatch.evaluate((element) => getComputedStyle(element).backgroundColor)
    await expect.poll(() => spoken.first().evaluate((element) =>
      getComputedStyle(element).boxShadow
    )).toContain(colour)
    await spoken.first().click()
    await expect(h.page.locator('.narration-panel')).toBeVisible()
    await expect(h.page.locator('.narration-panel-where')).toContainText('Harbour')
  } finally {
    await h.close()
  }
})
