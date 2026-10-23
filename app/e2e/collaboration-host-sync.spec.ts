import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook, type Harness } from './harness'

for (const outcome of ['finishes', 'fails', 'is canceled']) {
  test(`host pauses editing until an incoming join ${outcome}`, async () => {
    const writers: Harness[] = []
    try {
      const host = await launchApp('nl-sync-host-')
      writers.push(host)
      const project = await seedBook(host, { Opening: ['Arrival'] })
      const chapter = project.chapters.find(chapter => chapter.title === 'Opening')!
      const scene = chapter.scenes[0]
      await dismissTour(host.page)
      await host.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Morgan'])
      await host.page.evaluate(({ chapter, scene }) => window.novalistStores.project.getState().openScene(chapter, scene),
        { chapter: chapter.guid, scene: scene.id })
      const link = await host.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])
      const guest = await launchApp('nl-sync-guest-')
      writers.push(guest)
      await host.page.evaluate(cancel => {
        const observer = new MutationObserver(() => {
          const dialog = document.querySelector('[role="dialog"]')
          if (!dialog?.textContent?.includes('A collaborator is joining')) return
          document.body.dataset.hostSyncFrozen = String(document.querySelector('.shell, .app-shell')?.hasAttribute('inert'))
          observer.disconnect()
          if (cancel) dialog.querySelector<HTMLButtonElement>('button')?.click()
        })
        observer.observe(document.body, { childList: true, subtree: true, attributes: true })
      }, outcome === 'is canceled')
      const editor = host.page.frameLocator('iframe[title="editor"]').locator('#editor')
      await editor.fill('Saved before synchronization.')
      const joining = guest.rpc('collaboration/peer/join', [link,
        outcome === 'fails' ? guest.workDir : join(guest.workDir, 'Joined'), 'Alex'])
      if (outcome !== 'finishes') await expect(joining).rejects.toThrow()
      else await joining
      await expect(host.page.locator('body')).toHaveAttribute('data-host-sync-frozen', 'true')
      await expect(host.page.locator('.shell, .app-shell')).not.toHaveAttribute('inert', '')
      await expect(host.page.getByRole('dialog', { name: 'Project synchronization', exact: true })).toHaveCount(0)
      await expect(editor).toContainText('Saved before synchronization.')
      await editor.fill('Editing resumed.')
      if (outcome === 'finishes') {
        await expect.poll(async () => (await guest.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])).html)
          .toContain('Editing resumed.')
      }
    } finally { for (const writer of writers.reverse()) await writer.close() }
  })
}

test('two incoming writers synchronize in turn without leaving the host frozen', async () => {
  const writers: Harness[] = []
  try {
    const host = await launchApp('nl-sync-queue-host-')
    writers.push(host)
    await seedBook(host, { Opening: ['Arrival'] })
    await dismissTour(host.page)
    await host.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Morgan'])
    for (const name of ['Alex', 'Bea']) writers.push(await launchApp(`nl-sync-queue-${name}-`))
    const links = await Promise.all(writers.slice(1).map(() =>
      host.rpc<string>('collaboration/invite', [new Date(Date.now() + 60_000).toISOString()])))
    await Promise.all(writers.slice(1).map((guest, index) =>
      guest.rpc('collaboration/peer/join', [links[index], join(guest.workDir, 'Joined'), ['Alex', 'Bea'][index]])))
    await expect(host.page.locator('.shell, .app-shell')).not.toHaveAttribute('inert', '')
    await expect(host.page.getByRole('dialog', { name: 'Project synchronization', exact: true })).toHaveCount(0)
  } finally { for (const writer of writers.reverse()) await writer.close() }
})
