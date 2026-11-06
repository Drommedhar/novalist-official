import { writeFileSync, mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import { launchApp, seedBook } from '../harness'
import { identityProvider } from './identityProvider'
import { collaborationServer } from './collaborationServer'
import { mobileSmokeAccount } from './mobileSmokeAccount'

const host = await launchApp('novalist-mobile-smoke-host-', { NOVALIST_DISABLE_SELF_UPDATE: '1', NOVALIST_NO_CLIPBOARD: '1' })
const mode = process.env.NOVALIST_MOBILE_SMOKE_MODE === 'hosted' ? 'hosted' : 'tcp'
let subject = 'desktop'
let identity: Awaited<ReturnType<typeof identityProvider>> | undefined
let relay: Awaited<ReturnType<typeof collaborationServer>> | undefined
try {
  const directory = resolve('out/mobile-smoke')
  mkdirSync(directory, { recursive: true })
  const project = await seedBook(host, { Together: ['Cross platform'] })
  const chapter = project.chapters.find(value => value.title === 'Together')!
  const scene = chapter.scenes[0]
  await host.rpc('scenes/write', [chapter.guid, scene.id, '<p>Desktop <strong>formatting</strong>.</p>', 'Desktop formatting.'])
  if (mode === 'hosted') {
    identity = await identityProvider(() => subject, value => value + ' writer', 42683, false)
    relay = await collaborationServer(identity.address, 42684)
    await host.app.evaluate(({ shell }) => { shell.openExternal = async url => { await fetch(url) } })
    await host.rpc('collaboration/account/signIn', [relay.address])
    await host.rpc('collaboration/create', [relay.address])
    subject = 'mobile'
    writeFileSync(resolve(directory, 'credentials.json'), JSON.stringify(await mobileSmokeAccount(relay.address, identity.address)))
  } else await host.rpc('collaboration/peer/host', ['127.0.0.1', 42682, 'Desktop writer'])
  const invitation = await host.rpc<string>('collaboration/invite', [new Date(Date.now() + 600_000).toISOString()])
  writeFileSync(resolve(directory, 'config.json'), JSON.stringify({ mode, server: relay?.address, invitation, chapter: chapter.guid, scene: scene.id }))
  process.stdout.write('Desktop host ready.\n')
  for (let attempt = 0; attempt < 1800; attempt++) {
    const content = await host.rpc<{ html: string }>('scenes/read', [chapter.guid, scene.id]).catch(error => {
      if (String(error).includes('The workspace changed.')) return null
      throw error
    })
    if (!content) { await delay(500); continue }
    if (content.html.includes('Mobile edit.')) {
      await host.page.evaluate(async ({ chapter, scene }) => {
        await window.novalistStores.project.getState().openScene(chapter, scene)
      }, { chapter: chapter.guid, scene: scene.id })
      const editor = host.page.frameLocator('.editor-frame').locator('#editor')
      await editor.click()
      await host.page.keyboard.press('Control+End')
      await host.page.keyboard.type(' Desktop reply.')
      process.stdout.write('Desktop received mobile edit and sent a reply.\n')
      await delay(60_000)
      break
    }
    await delay(500)
  }
} finally { await host.close(); await relay?.close(); await identity?.close() }
