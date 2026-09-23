import { expect, test } from '@playwright/test'
import { join } from 'node:path'
import { dismissTour, launchApp, resizeWindow, seedBook, type Book } from './harness'

test('restoring a kept version removes later scenes and clears stale editor state', async () => {
  const h = await launchApp('nl-restore-version-')
  try {
    await h.rpc('settings/updateGlobal', [{ backupFolder: join(h.workDir, 'backups'), backupEnabled: false }])
    const book = await seedBook(h, { 'Chapter 1': ['Scene 1'] })
    await dismissTour(h.page)
    const chapter = book.chapters[0]
    const scene = chapter.scenes[0]
    await h.rpc('scenes/write', [chapter.guid, scene.id, '<p>Version one.</p>', 'Version one.'])
    await h.rpc('backup/createMilestone', ['Version 1'])
    const later = await h.rpc<Book>('project/createChapter', ['Chapter 2'])
    await h.rpc('project/createScene', [later.chapters[1].guid, 'Scene 2'])
    await h.rpc('backup/createMilestone', ['Version 2'])
    await h.page.evaluate(async ({ chapterGuid, sceneId }) => {
      const project = window.novalistStores.project.getState()
      const path = project.projectPath!
      await project.closeProject()
      await window.novalistStores.project.getState().openProject(path)
      await window.novalistStores.project.getState().openScene(chapterGuid, sceneId)
      window.novalistStores.shell.getState().openSettings()
    }, { chapterGuid: chapter.guid, sceneId: scene.id })
    await h.page.locator('.settings-nav-item', { hasText: 'Backups' }).click()
    const version1 = h.page.locator('.backup-row', { hasText: 'Version 1' })
    await expect(version1).toBeVisible()
    h.page.on('dialog', (dialog) => void dialog.accept())

    // Queue an editor save and click Restore in the same turn, before autosave.
    await version1.evaluate((row) => {
      const project = window.novalistStores.project.getState()
      project.onEditorContentChanged(project.activeEditorPaneId!, '<p>Pending version two.</p>', 'Pending version two.')
      const button = [...row.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Restore')!
      button.click()
    })
    await expect.poll(() => h.page.evaluate(() => window.novalistStores.project.getState().chapters.length)).toBe(1)
    expect(await h.page.evaluate(() => {
      const s = window.novalistStores.project.getState()
      return { editors: s.editors, tabs: s.openTabs, hashes: s.sceneHashes, scene: s.openSceneId }
    })).toEqual({ editors: {}, tabs: [], hashes: {}, scene: null })
    expect((await h.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])).html).toContain('Version one.')

    // The flushed edit belongs in the safety backup, never in the restored scene.
    const backups = await h.rpc<{ trigger: string; path: string }[]>('backup/list')
    const safety = backups.find((b) => b.trigger === 'prerestore')!
    expect(safety).toBeTruthy()
    await h.rpc('backup/restoreAsNewProject', [safety.path, h.workDir, 'Safety copy'])
    expect((await h.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id])).html).toContain('Pending version two.')
  } finally {
    await h.close()
  }
})

for (const mobile of [false, true]) {
  test(`restore a ZIP as a new project from the ${mobile ? 'phone' : 'desktop'} welcome screen`, async () => {
    const h = await launchApp('nl-restore-copy-', mobile ? { NOVALIST_FORCE_MOBILE: '1' } : {})
    try {
      if (mobile) await resizeWindow(h, 393, 852)
      await h.rpc('settings/updateGlobal', [{ backupFolder: join(h.workDir, 'backups'), backupEnabled: false }])
      const book = await seedBook(h, { 'Original chapter': ['Original scene'] })
      await dismissTour(h.page)
      const backup = await h.rpc<{ path: string }>('backup/createMilestone', ['Version 1'])
      await h.rpc('project/createChapter', ['Later chapter'])
      const original = await h.page.evaluate(async () => {
        const project = window.novalistStores.project.getState()
        const path = project.projectPath!
        await project.closeProject()
        return path
      })
      await expect(h.page.locator('.start-screen')).toBeVisible()
      await h.page.getByRole('button', { name: 'Restore backup as new project', exact: true }).click()
      const dialog = h.page.getByRole('dialog', { name: 'Restore backup as new project' })
      await h.app.evaluate(({ dialog }, paths) => {
        dialog.showOpenDialog = (async (...args: unknown[]) => {
          const options = args.at(-1) as { properties?: string[] }
          return { canceled: false, filePaths: [options.properties?.includes('openDirectory') ? paths.parent : paths.zip] }
        }) as typeof dialog.showOpenDialog
      }, { parent: h.workDir, zip: backup.path })
      await dialog.getByRole('button', { name: 'Browse...' }).nth(0).click()
      await dialog.getByLabel('Project Name', { exact: true }).fill('Restored copy')
      await dialog.getByRole('button', { name: 'Browse...' }).nth(1).click()
      await dialog.getByRole('button', { name: 'Restore and open' }).click()
      await expect(dialog).toHaveCount(0)
      expect(await h.page.evaluate(() => {
        const project = window.novalistStores.project.getState()
        return { name: project.projectName, chapters: project.chapters.map((c) => c.guid) }
      })).toEqual({ name: 'Restored copy', chapters: [book.chapters[0].guid] })

      await h.page.evaluate((path) => window.novalistStores.project.getState().openProject(path), original)
      expect((await h.rpc<Book>('project/getState')).chapters.map((c) => c.title))
        .toEqual(['Original chapter', 'Later chapter'])
    } finally {
      await h.close()
    }
  })
}
