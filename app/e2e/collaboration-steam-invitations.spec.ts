import { expect, test } from '@playwright/test'
import { cpSync } from 'node:fs'
import { join } from 'node:path'
import { dismissTour, launchApp, seedBook } from './harness'
import { deepLinkFromArgv, parseDeepLink } from '../src/main/deeplink'

test('Steam launch arguments are routed as invitations and reject malformed destinations', () => {
  const link = 'novalist://collaboration/steam-invite#' + 'A'.repeat(159)
  expect(deepLinkFromArgv(['Novalist.exe', link])).toEqual({ steamInvite: link })
  for (const invalid of [link.replace('#', '?project=C:/private#'), link + 'A', link.replace('collaboration', 'user@collaboration'), link.slice(0, -1)]) {
    expect(parseDeepLink(invalid)).toBeNull()
  }
})

test('the Steam friends picker sends only to the selected friend after confirmation', async () => {
  const host = await launchApp('nl-steam-friends-picker-')
  try {
    await seedBook(host, { Opening: ['Arrival'] })
    await host.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Morgan'])
    await host.page.evaluate(() => {
      const request = window.novalistRpc.request.bind(window.novalistRpc)
      window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
        if (method === 'collaboration/status') return { ...await request<Record<string, unknown>>(method, params), mode: 'steam' } as T
        if (method === 'collaboration/steam/friends') return [
          { steamId: '76561198000000001', name: 'Alex', online: true },
          { steamId: '76561198000000002', name: 'Sam', online: false }
        ] as T
        if (method === 'collaboration/steam/inviteFriend') {
          document.body.dataset.invitedSteamFriend = String(Array.isArray(params) ? params[0] : '')
          return undefined as T
        }
        return request<T>(method, params)
      }
      window.novalistStores.shell.getState().openSettings('collaboration')
    })
    await host.page.getByRole('button', { name: 'Invite Steam friends', exact: true }).click()
    const send = host.page.getByRole('button', { name: 'Send Steam invitation', exact: true })
    await expect(send).toBeDisabled()
    await host.page.getByLabel('Choose a Steam friend', { exact: true }).selectOption('76561198000000002')
    await expect(host.page.locator('body')).not.toHaveAttribute('data-invited-steam-friend')
    await send.click()
    await expect(host.page.locator('body')).toHaveAttribute('data-invited-steam-friend', '76561198000000002')
    await expect(host.page.getByText('Invitation sent through Steam. Keep this session open while your friend joins.', { exact: true })).toBeVisible()
  } finally { await host.close() }
})

for (const existing of [false, true]) {
  test(`Steam invitation asks before joining into ${existing ? 'an existing' : 'a new'} local copy`, async () => {
    const host = await launchApp('nl-steam-invite-host-')
    const guest = await launchApp('nl-steam-invite-guest-')
    try {
      await seedBook(host, { Opening: ['Arrival'] })
      await host.rpc('collaboration/peer/host', ['127.0.0.1', 0, 'Morgan'])
      const link = await host.rpc<string>('collaboration/invite', [new Date(Date.now() + 3_600_000).toISOString()])
      const destination = join(guest.workDir, 'Joined')
      if (existing) {
        const root = await host.page.evaluate(() => window.novalistStores.project.getState().projectPath)
        cpSync(root!, destination, { recursive: true })
      }
      await dismissTour(guest.page)
      await guest.page.evaluate(link => {
        const request = window.novalistRpc.request.bind(window.novalistRpc)
        let pending: unknown = { id: 'steam-invite-test', link, friendName: 'Morgan', error: null }
        window.novalistRpc.request = async <T,>(method: string, params?: unknown): Promise<T> => {
          if (method === 'collaboration/steam/pendingInvitation') return pending as T
          if (method === 'collaboration/steam/status') return { available: true, displayName: 'Alex' } as T
          if (method === 'collaboration/steam/dismissInvitation') { pending = null; return undefined as T }
          if (method === 'collaboration/steam/join') {
            document.body.dataset.steamJoinStarted = 'true'
            return request<T>('collaboration/peer/join', params)
          }
          return request<T>(method, params)
        }
      }, link)
      const dialog = guest.page.getByRole('dialog', { name: 'Steam project invitation', exact: true })
      await expect(dialog.getByText('Morgan invited you to collaborate.')).toBeVisible()
      const accept = dialog.getByRole('button', { name: 'Accept and join', exact: true })
      await expect(accept).toBeDisabled()
      await expect(guest.page.locator('body')).not.toHaveAttribute('data-steam-join-started')
      await guest.app.evaluate(({ dialog }) => {
        dialog.showOpenDialog = async () => ({ canceled: true, filePaths: [] })
      })
      await dialog.getByRole('button', { name: 'Choose parent folder', exact: true }).click()
      await expect(accept).toBeDisabled()
      await guest.app.evaluate(({ dialog }, folder) => {
        dialog.showOpenDialog = async () => ({ canceled: false, filePaths: [folder] })
      }, existing ? destination : guest.workDir)
      if (existing) await dialog.getByLabel('Use an existing local project', { exact: true }).check()
      else await dialog.getByLabel('Local project folder name', { exact: true }).fill('Joined')
      await dialog.getByRole('button', { name: existing ? 'Choose existing project folder' : 'Choose parent folder', exact: true }).click()
      await expect(accept).toBeEnabled()
      await expect(guest.page.locator('body')).not.toHaveAttribute('data-steam-join-started')
      await accept.click()
      await expect(dialog).toHaveCount(0)
      await expect.poll(() => guest.page.evaluate(() => window.novalistStores.project.getState().projectPath)).toBe(destination)
      await expect.poll(() => guest.page.evaluate(() => window.novalistStores.project.getState().chapters[0]?.scenes[0]?.title)).toBe('Arrival')
    } finally { await guest.close(); await host.close() }
  })
}
