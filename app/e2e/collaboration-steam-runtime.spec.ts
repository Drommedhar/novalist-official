import { expect, test } from '@playwright/test'
import { resolve } from 'node:path'
import { dismissTour, launchApp, seedBook } from './harness'

test.skip(process.env.NOVALIST_STEAM_INTEGRATION !== '1', 'Requires the Steam build and a running entitled Steam client.')

function launchInvitation(): string {
  const bytes = Buffer.alloc(119, 1)
  bytes.writeBigUInt64LE(76561198000000001n, 1)
  bytes.writeUInt32LE(0, 41)
  bytes.writeBigInt64LE(BigInt(Math.floor(Date.now() / 1000) + 3600), 109)
  bytes.writeUInt16LE(49152, 117)
  return 'novalist://collaboration/steam-invite#' + bytes.toString('base64url')
}

test('Steam initializes, reads friends, hosts again after ending and receives launch invitations', async () => {
  const appId = process.env.NOVALIST_STEAM_APP_ID ?? '480'
  const app = await launchApp('nl-steam-runtime-', {
    NOVALIST_BACKEND_PATH: resolve('out/collaboration-steam-build/bin/Novalist.Backend/debug/Novalist.Backend.exe'),
    NOVALIST_STEAM_APP_ID: appId, SteamAppId: appId, SteamGameId: appId
  })
  try {
    const status = await app.rpc<{ available: boolean; reason: string }>('collaboration/steam/status')
    expect(status.available, status.reason).toBe(true)
    expect(Array.isArray(await app.rpc('collaboration/steam/friends'))).toBe(true)
    await seedBook(app, { Opening: ['Arrival'] })
    const session = await app.rpc<{ mode: string; isOwner: boolean }>('collaboration/steam/host', ['Steam test writer'])
    expect(session.mode).toBe('steam')
    expect(session.isOwner).toBe(true)
    await app.rpc('collaboration/end')
    const after = await app.rpc<{ available: boolean; reason: string }>('collaboration/steam/status')
    expect(after.available, after.reason).toBe(true)
    await app.rpc('collaboration/steam/host', ['Steam test writer'])
    await app.rpc('collaboration/end')
    await dismissTour(app.page)
    await app.app.evaluate(({ app }, invitation) => {
      app.emit('second-instance', {}, ['Novalist.exe', invitation], process.cwd())
    }, launchInvitation())
    const dialog = app.page.getByRole('dialog', { name: 'Steam project invitation', exact: true })
    await expect(dialog).toBeVisible()
    await expect(dialog.getByRole('button', { name: 'Accept and join', exact: true })).toBeDisabled()
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
    await expect(dialog).toHaveCount(0)
    expect(await app.rpc('collaboration/steam/pendingInvitation')).toBeNull()
  } finally { await app.close() }
})

test('a Steam invitation delivered at launch waits for folder selection', async () => {
  const app = await launchApp('nl-steam-invite-cold-', {
    NOVALIST_BACKEND_PATH: resolve('out/collaboration-steam-build/bin/Novalist.Backend/debug/Novalist.Backend.exe'),
    NOVALIST_STEAM_APP_ID: '480', SteamAppId: '480', SteamGameId: '480'
  }, undefined, [launchInvitation()])
  try {
    const dialog = app.page.getByRole('dialog', { name: 'Steam project invitation', exact: true })
    await expect(dialog).toBeVisible()
    await expect(dialog.getByRole('button', { name: 'Accept and join', exact: true })).toBeDisabled()
    expect(await app.page.evaluate(() => window.novalistStores.project.getState().isLoaded)).toBe(false)
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
    await expect(dialog).toHaveCount(0)
    expect(await app.rpc('collaboration/status')).toBeNull()
  } finally { await app.close() }
})
