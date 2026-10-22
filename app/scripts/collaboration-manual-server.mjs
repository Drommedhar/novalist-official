import { createInterface } from 'node:readline'
import { stdin, stdout } from 'node:process'
import { fileURLToPath } from 'node:url'
import { identityProvider } from '../e2e/fixtures/identityProvider.ts'
import { collaborationServer } from '../e2e/fixtures/collaborationServer.ts'

process.chdir(fileURLToPath(new URL('..', import.meta.url)))
const accounts = { owner: 'Morgan', guest: 'Alex', third: 'Sam' }
let account = 'owner'
const identity = await identityProvider(() => account, subject => accounts[subject])
let server
try {
  server = await collaborationServer(identity.address)
  console.log(`Local test server: ${server.address}`)
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
