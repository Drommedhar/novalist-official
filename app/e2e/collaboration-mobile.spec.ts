import { expect, test } from '@playwright/test'
import { dismissTour, launchApp, seedBook } from './harness'

test('the mobile editor leaves a live session and keeps saving locally', async () => {
  const mobile = await launchApp('novalist-mobile-session-', { NOVALIST_FORCE_MOBILE: '1' })
  try {
    const project = await seedBook(mobile, { Chapter: ['Scene'] })
    const chapter = project.chapters.find(value => value.title === 'Chapter')!
    const scene = chapter.scenes[0]
    await mobile.rpc('scenes/write', [chapter.guid, scene.id, '<p>Before sharing.</p>', 'Before sharing.'])
    await dismissTour(mobile.page)
    await mobile.page.evaluate(async ({ chapter, scene }) => {
      window.novalistStores.shell.getState().setMobileTab('manuscript')
      await window.novalistStores.project.getState().openScene(chapter, scene)
    }, { chapter: chapter.guid, scene: scene.id })
    const editor = mobile.page.frameLocator('.editor-frame').locator('#editor')
    await expect(editor).toHaveText('Before sharing.')
    await mobile.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Phone writer'])
    await expect(editor).toHaveText('Before sharing.')
    await editor.click()
    await mobile.page.keyboard.press('Control+End')
    await mobile.page.keyboard.type(' Shared.')
    await mobile.rpc('collaboration/end')
    await expect(editor).toHaveText('Before sharing. Shared.')
    await editor.click()
    await mobile.page.keyboard.press('Control+End')
    await mobile.page.keyboard.type(' Local again.')
    await expect.poll(async () => (await mobile.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])).html)
      .toContain('Local again.')
    expect((await mobile.rpc<{ shared: unknown }>('scenes/read', [chapter.guid, scene.id])).shared).toBeNull()
  } finally { await mobile.close() }
})
