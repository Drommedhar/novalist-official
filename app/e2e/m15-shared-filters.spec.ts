import { test, expect } from '@playwright/test'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

async function openFilteredBook(h: Harness): Promise<void> {
  const book = await seedBook(h, {
    'Outline chapter': ['Ready in outline', 'Needs revision'],
    'Final chapter': ['Ready in final']
  })
  const [outline, final] = book.chapters
  await h.rpc('project/setChapterStatus', [final.guid, 'Final'])
  await h.rpc('stages/setSceneStage', [outline.guid, outline.scenes[0].id, 'final'])
  await h.rpc('stages/setSceneStage', [outline.guid, outline.scenes[1].id, 'revised'])
  await h.rpc('stages/setSceneStage', [final.guid, final.scenes[0].id, 'final'])
  await dismissTour(h.page)
  await h.page.evaluate(() => {
    window.novalistStores.manuscript.getState().setMode('outliner')
    window.novalistStores.shell.getState().setMainView('manuscript')
  })
  await expect(h.page.locator('.outliner-scene-title')).toHaveText([
    'Ready in outline', 'Needs revision', 'Ready in final'
  ])
  await h.page.locator('.filter-bar-toggle').click()
}

test('scene stages combine with chapter status and survive clearing, presets and navigation', async () => {
  const h = await launchApp('nl-shared-filters-')
  try {
    await openFilteredBook(h)
    const status = h.page.getByRole('combobox', { name: 'Status', exact: true })
    const stage = h.page.getByRole('combobox', { name: 'Stage', exact: true })
    const titles = h.page.locator('.outliner-scene-title')

    await stage.selectOption('final')
    await expect(titles).toHaveText(['Ready in outline', 'Ready in final'])
    await status.selectOption('Final')
    await expect(titles).toHaveText(['Ready in final'])
    await h.page.getByRole('button', { name: 'Clear 2', exact: true }).click()
    await expect(titles).toHaveText(['Ready in outline', 'Needs revision', 'Ready in final'])

    await stage.selectOption('revised')
    await expect(titles).toHaveText(['Needs revision'])
    await h.page.locator('.filter-presets input').fill('Revision pass')
    await h.page.locator('.filter-presets input').press('Enter')
    await stage.selectOption('final')
    await expect(titles).toHaveText(['Ready in outline', 'Ready in final'])
    await h.page.getByRole('button', { name: 'Revision pass', exact: true }).click()
    await expect(titles).toHaveText(['Needs revision'])

    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('timeline'))
    await expect(stage).toHaveValue('revised')
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('manuscript'))
    await expect(titles).toHaveText(['Needs revision'])
  } finally {
    await h.close()
  }
})

test('manuscript presents one status filter instead of conflicting duplicate controls', async () => {
  const h = await launchApp('nl-filter-controls-')
  try {
    await openFilteredBook(h)
    await expect(h.page.getByRole('combobox', { name: 'Status', exact: true })).toHaveCount(1)
    await expect(h.page.locator('.manuscript-toolbar').getByRole('button', { name: 'Final', exact: true })).toHaveCount(0)
  } finally {
    await h.close()
  }
})

test('manuscript loads the shared status and scene stage set in another view', async () => {
  const h = await launchApp('nl-filter-state-')
  try {
    await openFilteredBook(h)
    await h.page.evaluate(() => window.novalistStores.shell.getState().setMainView('timeline'))
    await h.page.locator('.filter-bar-toggle').click()
    await h.page.getByRole('combobox', { name: 'Status', exact: true }).selectOption('Final')
    await h.page.getByRole('combobox', { name: 'Stage', exact: true }).selectOption('final')

    // Loading the manuscript must use the same filter even before its view mounts.
    const titles = await h.page.evaluate(async () => {
      const manuscript = window.novalistStores.manuscript
      await manuscript.getState().load()
      return manuscript.getState().sections.flatMap((section) => section.scenes.map((scene) => scene.title))
    })
    expect(titles).toEqual(['Ready in final'])
  } finally {
    await h.close()
  }
})
