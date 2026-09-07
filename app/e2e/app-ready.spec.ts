import { expect, test, type Page } from '@playwright/test'
import { evaluateWhenReady } from './appReady'
import { launchApp, seedBook, shapeOf, state } from './harness'

const contextLost = 'Execution context was destroyed, most likely because of a navigation'

/** Lose one transport reply after a real side effect has reached the renderer. */
function loseReplyAfter(page: Page, happened: () => Promise<boolean>, reply = 1): () => boolean {
  let lost = false
  let replies = 0
  const maybeLose = async (): Promise<void> => {
    if (!lost && await happened() && ++replies === reply) {
      lost = true
      throw new Error(contextLost)
    }
  }
  const evaluate = page.evaluate.bind(page)
  page.evaluate = (async (...args: Parameters<Page['evaluate']>) => {
    const result = await evaluate(...args)
    await maybeLose()
    return result
  }) as Page['evaluate']

  const evaluateHandle = page.evaluateHandle.bind(page)
  page.evaluateHandle = (async (...args: Parameters<Page['evaluateHandle']>) => {
    const handle = await evaluateHandle(...args)
    const evaluate = handle.evaluate.bind(handle)
    handle.evaluate = (async (...args: Parameters<typeof handle.evaluate>) => {
      const result = await evaluate(...args)
      await maybeLose()
      return result
    }) as typeof handle.evaluate
    return handle
  }) as Page['evaluateHandle']
  return () => lost
}

for (const [phase, reply] of [['start', 1], ['result', 2]] as const) {
  test(`a lost ${phase} reply returns the original result without repeating its side effect`, async ({ page }) => {
    const read = page.evaluate.bind(page)
    const lost = loseReplyAfter(page, () => read(() => Boolean(document.body.dataset.runs)), reply)
    const result = await evaluateWhenReady(page, async (value) => {
      document.body.dataset.runs = String(Number(document.body.dataset.runs ?? 0) + 1)
      await new Promise((resolve) => setTimeout(resolve, 50))
      return { value, runs: Number(document.body.dataset.runs) }
    }, 'original')

    expect(lost()).toBe(true)
    expect(result).toEqual({ value: 'original', runs: 1 })
    expect(await read(() => document.body.dataset.runs)).toBe('1')
  })
}

test('a lost function lookup can retry before the callback starts', async ({ page }) => {
  const evaluateHandle = page.evaluateHandle.bind(page)
  let lookups = 0
  page.evaluateHandle = (async (...args: Parameters<Page['evaluateHandle']>) => {
    if (++lookups === 1) throw new Error(contextLost)
    return evaluateHandle(...args)
  }) as Page['evaluateHandle']
  expect(await evaluateWhenReady(page, () => {
    document.body.dataset.runs = String(Number(document.body.dataset.runs ?? 0) + 1)
    return Number(document.body.dataset.runs)
  })).toBe(1)
  expect(lookups).toBe(2)
})

test('a real navigation fails the pending action without replaying it in the new document', async ({ page }) => {
  const pending = evaluateWhenReady(page, async () => {
    document.body.dataset.runs = '1'
    await new Promise(() => {})
  })
  // Attach the rejection handler before navigating, while the action is pending.
  const rejected = expect(pending).rejects.toThrow()
  await expect(page.locator('body')).toHaveAttribute('data-runs', '1')
  await page.goto('data:text/html,<body>New document</body>')
  await rejected
  await expect(page.locator('body')).not.toHaveAttribute('data-runs')
})

test('a callback error is reported once even when its message resembles a transport error', async ({ page }) => {
  await expect(evaluateWhenReady(page, (message) => {
    document.body.dataset.runs = String(Number(document.body.dataset.runs ?? 0) + 1)
    throw new Error(message)
  }, contextLost)).rejects.toThrow(contextLost)
  expect(await page.evaluate(() => document.body.dataset.runs)).toBe('1')
})

test('losing a scene creation reply does not leave a duplicate scene when another is archived', async () => {
  const h = await launchApp('nl-create-reply-')
  try {
    const book = await seedBook(h, { One: [] })
    const chapter = book.chapters[0]
    const read = h.page.evaluate.bind(h.page)
    const lost = loseReplyAfter(h.page, () => read(async () => {
      const book = await window.novalistRpc.request('project/getState') as {
        chapters: { scenes: unknown[] }[]
      }
      return book.chapters[0].scenes.length > 0
    }))

    await h.rpc('project/createScene', [chapter.guid, 'Keep'])
    await h.rpc('project/createScene', [chapter.guid, 'Park'])
    expect(lost()).toBe(true)
    const before = await state(h)
    expect(shapeOf(before)).toEqual({ One: ['Keep', 'Park'] })
    const park = before.chapters[0].scenes.find((scene) => scene.title === 'Park')!
    await h.rpc('sceneBulk/archive', [[park.id]])
    expect(shapeOf(await state(h))).toEqual({ One: ['Keep'] })
  } finally {
    await h.close()
  }
})
