import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { launchApp, dismissTour } from '../harness'
import { expect } from '@playwright/test'

const config = JSON.parse(readFileSync('out/mobile-smoke/mobile-host.json', 'utf8')) as { invitation: string; chapter: string; scene: string }
const guest = await launchApp('novalist-mobile-host-guest-', { NOVALIST_DISABLE_SELF_UPDATE: '1', NOVALIST_NO_CLIPBOARD: '1' })
try {
  await guest.rpc('collaboration/peer/join', [config.invitation, join(guest.workDir, 'Joined'), 'Desktop guest'])
  await dismissTour(guest.page)
  await guest.page.evaluate(async ({ chapter, scene }) => {
    await window.novalistStores.project.getState().openScene(chapter, scene)
  }, config)
  const editor = guest.page.frameLocator('.editor-frame').locator('#editor')
  await expect(editor).toHaveText('Hosted on mobile.')
  await editor.click()
  await guest.page.keyboard.press('Control+End')
  await guest.page.keyboard.type(' Desktop joined the mobile host.')
  await expect.poll(async () => (await guest.rpc<{ html: string }>('scenes/read', [config.chapter, config.scene])).html)
    .toContain('Desktop joined the mobile host.')
  process.stdout.write('Desktop joined the iPhone host and edited the shared scene.\n')
} finally { await guest.close() }
