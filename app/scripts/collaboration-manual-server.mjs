import { createInterface } from 'node:readline'
import { stdin, stdout } from 'node:process'
import { fileURLToPath } from 'node:url'
import { identityProvider } from '../e2e/fixtures/identityProvider.ts'
import { collaborationServer } from '../e2e/fixtures/collaborationServer.ts'

process.chdir(fileURLToPath(new URL('..', import.meta.url)))
const accounts = { owner: 'Morgan', guest: 'Alex', third: 'Sam' }
let account = 'owner'
const port = (name, fallback) => {
  const value = Number(process.env[name] ?? fallback)
  if (!Number.isInteger(value) || value < 1 || value > 65535) throw new Error(`${name} must be a port between 1 and 65535.`)
  return value
}
const relayPort = port('NOVALIST_COLLAB_RELAY_PORT', 42670)
const identityPort = port('NOVALIST_COLLAB_IDENTITY_PORT', 42671)
if (relayPort === identityPort) throw new Error('The relay and sign-in provider need different ports.')
const identity = await identityProvider(() => account, subject => accounts[subject], identityPort, false)
let server
try {
  server = await collaborationServer(identity.address, relayPort)
  console.log(`Local test server: ${server.address}`)
  console.log('Use the Local test server URL above in Settings > Collaboration in both apps.')
  console.log(`Browser sign-in only (do not enter this in Settings): ${identity.address}`)
  console.log('After restarting this helper, sign out and sign in again in each app before sharing.')
  console.log('Next browser sign-in: Morgan (owner). No password is needed.')
  console.log('Enter owner, guest or third to choose the account for the next sign-in.')
  console.log('This server relays packets only; it stores no project data and needs no database.')
  console.log('Keep the host app and this terminal running for collaboration. Enter quit to stop the relay.')
  const terminal = createInterface({ input: stdin, output: stdout, prompt: 'account> ' })
  const stop = () => terminal.close()
  process.on('SIGINT', stop)
  process.on('SIGTERM', stop)
  terminal.prompt()
  try {
    for await (const line of terminal) {
      const command = line.trim().toLowerCase()
      if (command === 'quit') break
      if (Object.hasOwn(accounts, command)) {
        account = command
        console.log(`Next browser sign-in: ${accounts[account]} (${account}).`)
      } else console.log('Choose owner, guest, third, or quit.')
      terminal.prompt()
    }
  } finally {
    terminal.close()
    process.off('SIGINT', stop)
    process.off('SIGTERM', stop)
  }
} finally {
  try { await server?.close() }
  finally { await identity.close() }
}
