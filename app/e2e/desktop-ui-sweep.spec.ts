import { expect, test } from '@playwright/test'
import { dismissTour, enterWriting, launchApp, resizeWindow, seedBook } from './harness'

test('every binder scene link stays in Narration and leaves the editor selection alone', async () => {
  const h = await launchApp('nl-sweep-binder-')
  try {
    const book = await seedBook(h, { Opening: ['Arrival', 'Departure'], Ending: ['Return'] })
    const [opening, ending] = book.chapters
    for (const chapter of book.chapters) {
      for (const scene of chapter.scenes) {
        const paragraphs = Array.from({ length: 35 }, (_, i) => `${scene.title} passage ${i}.`)
        await h.rpc('scenes/write', [
          chapter.guid,
          scene.id,
          paragraphs.map((p) => `<p>${p}</p>`).join(''),
          paragraphs.join('\n')
        ])
      }
    }
    await h.rpc('collections/create', ['Listen again', [opening.scenes[1].id]])
    await h.rpc('smartLists/save', [
      null,
      'Departure scenes',
      'All',
      [{ field: 'title', op: 'Is', value: 'Departure' }]
    ])
    for (const bookmark of [
      {
        kind: 'Scene',
        label: 'Departure bookmark',
        chapterGuid: opening.guid,
        targetId: opening.scenes[1].id
      },
      { kind: 'Chapter', label: 'Ending bookmark', chapterGuid: ending.guid, targetId: null }
    ]) {
      await h.rpc('bookmarks/save', [
        {
          id: '',
          group: null,
          targetType: null,
          anchorText: null,
          storyDate: null,
          order: 0,
          ...bookmark
        }
      ])
    }
    await dismissTour(h.page)
    await enterWriting(h.page)
    await h.page.locator('.binder-scene-row').first().click()
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('narration'))
    const frame = h.page.frameLocator('.narration-frame')
    await expect(frame.locator('.nl-scene')).toHaveCount(3)
    for (const [tab, label, index] of [
      ['Collections', 'Departure', 1],
      ['Bookmarks', 'Ending bookmark', 2],
      ['Bookmarks', 'Departure bookmark', 1],
      ['Smart Lists', 'Departure', 1]
    ] as const) {
      await h.page.locator('.binder-tabs').getByRole('button', { name: tab, exact: true }).click()
      if (tab === 'Smart Lists')
        await h.page.getByRole('button', { name: 'Departure scenes', exact: true }).click()
      await frame.locator('#wrap').evaluate((el) => {
        el.scrollTop = 0
      })
      await h.page.locator('.binder-scene-row').filter({ hasText: label }).click()
      await expect(frame.locator('.nl-scene-title').nth(index)).toBeInViewport()
      expect(
        await h.page.evaluate(() => ({
          view: window.novalistStores.shell.getState().mainView,
          editor: window.novalistStores.project.getState().openSceneId
        }))
      ).toEqual({ view: 'narration', editor: opening.scenes[0].id })
    }
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('write'))
    await h.page.locator('.binder-scene-row').filter({ hasText: 'Departure' }).click()
    await expect
      .poll(() => h.page.evaluate(() => window.novalistStores.project.getState().openSceneId))
      .toBe(opening.scenes[1].id)
  } finally {
    await h.close()
  }
})

test('busy calendar days contain their events in a compact window', async () => {
  const h = await launchApp('nl-sweep-calendar-')
  try {
    const book = await seedBook(h, { Opening: ['Arrival', 'Discovery', 'Departure', 'Return'] })
    const chapter = book.chapters[0]
    for (const scene of chapter.scenes)
      await h.rpc('project/setSceneDateRange', [chapter.guid, scene.id, '2026-03-14', '', ''])
    await h.rpc('calendar/setAnchor', ['2026-03-14'])
    await dismissTour(h.page)
    await resizeWindow(h, 960, 720)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('calendar'))
    const day = h.page
      .locator('.calendar-cell')
      .filter({ has: h.page.locator('.calendar-overflow') })
    await expect(day.locator('.calendar-event')).toHaveCount(3)
    await expect(day.locator('.calendar-overflow')).toHaveText('+1')
    await day.scrollIntoViewIfNeeded()
    expect(
      await day.evaluate((el) =>
        [...el.children].every(
          (child) => child.getBoundingClientRect().bottom <= el.getBoundingClientRect().bottom
        )
      )
    ).toBe(true)
    await day.locator('.calendar-event').nth(2).click()
    await expect
      .poll(() => h.page.evaluate(() => window.novalistStores.project.getState().openSceneId))
      .toBe(chapter.scenes[2].id)
  } finally {
    await h.close()
  }
})

test('wiki profiles and long voice names fit compact desktop panes', async () => {
  const h = await launchApp('nl-sweep-compact-')
  try {
    await seedBook(h, { Opening: ['Arrival'] })
    const entry = await h.rpc<{ id: string }>('entities/create', ['character', 'Amy Calder'])
    await h.rpc('entities/update', [
      'character',
      entry.id,
      { surname: 'Calder', notes: 'The photographer who notices the missing letter.' }
    ])
    await dismissTour(h.page)
    await resizeWindow(h, 960, 720)
    await h.page.evaluate(async (id) => {
      window.novalistStores.shell.getState().setMainView('wiki')
      await window.novalistStores.wiki.getState().openArticle('character', id)
    }, entry.id)
    await expect(h.page.locator('.wiki-infobox')).toBeVisible()
    await expect(h.page.locator('.wiki-article-content')).toBeVisible()
    expect(
      await h.page.locator('.wiki-main').evaluate((el) => el.scrollWidth <= el.clientWidth)
    ).toBe(true)
    const info = await h.page.locator('.wiki-infobox').boundingBox()
    const article = await h.page.locator('.wiki-article-content').boundingBox()
    expect(info!.y).toBeGreaterThanOrEqual(article!.y + article!.height)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('settings'))
    await h.page
      .locator('.settings-nav')
      .getByRole('button', { name: 'Editor', exact: true })
      .click()
    const voice = h.page.locator('#set-readaloud-voice')
    await expect(voice).toBeVisible()
    // Native voice names vary by operating system. Reproduce a long installed name.
    await voice.evaluate((el) =>
      el.append(
        new Option(
          'Microsoft very long multilingual voice name — German (Germany) — Natural Desktop Voice',
          'long-test-voice'
        )
      )
    )
    expect(await voice.evaluate((el) => el.getBoundingClientRect().right <= innerWidth)).toBe(true)
    expect(
      await h.page.locator('.main-area').evaluate((el) => el.scrollWidth <= el.clientWidth)
    ).toBe(true)
    await h.page.locator('.settings-sections').evaluate((el) => {
      el.scrollTop = el.scrollHeight
    })
    await h.page.locator('.settings-nav button').last().click()
    await expect(h.page.locator('.settings-section-title')).toBeInViewport()
    await expect(h.page.locator('.settings-header')).toBeInViewport()
    expect(await h.page.locator('.settings-sections').evaluate((el) => el.scrollTop)).toBe(0)
    const nav = h.page.locator('.settings-nav')
    await nav.getByRole('button', { name: 'Editor', exact: true }).click()
    await expect(h.page.locator('#set-compose-dimming')).toHaveCount(1)
    await nav.getByRole('button', { name: 'Writing assistance', exact: true }).click()
    await expect(h.page.locator('#set-compose-dimming')).toHaveCount(0)
    await nav.getByRole('button', { name: 'Writing goals', exact: true }).click()
    await expect(h.page.locator('#set-compose-dimming')).toHaveCount(0)
    await nav.getByRole('button', { name: 'Templates', exact: true }).click()
    await expect(h.page.locator('#set-compose-dimming')).toHaveCount(0)
  } finally {
    await h.close()
  }
})
