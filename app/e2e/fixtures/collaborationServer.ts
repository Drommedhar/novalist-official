import { spawn, type ChildProcess } from 'node:child_process'
import { once } from 'node:events'
import { createServer } from 'node:net'
import { resolve } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'

export async function collaborationServer(authority: string, port = 0): Promise<{ address: string; close(): Promise<void> }> {
  const source = resolve('out/collaboration-sync-server')
  const reservation = createServer()
  reservation.listen(port, '127.0.0.1')
  await once(reservation, 'listening')
  const endpoint = reservation.address()
  if (!endpoint || typeof endpoint === 'string') throw new Error('The test server has no port.')
  const address = `http://127.0.0.1:${endpoint.port}`
  await new Promise<void>((done, reject) => reservation.close(error => error ? reject(error) : done()))
  const env = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: address,
    Relay__Enabled: 'true',
    Authentication__Schemes__Bearer__Authority: authority,
    Authentication__Schemes__Bearer__ValidAudiences__0: 'novalist',
    Authentication__Schemes__Bearer__RequireHttpsMetadata: 'false',
    Authentication__NativeClient__ClientId: 'novalist',
    Authentication__NativeClient__Scope: 'openid profile sync offline_access',
    Collaboration__ServerUrl: address
  }
  let child: ChildProcess | undefined
  const close = async (): Promise<void> => {
    if (child && child.exitCode === null) {
      const exited = once(child, 'exit')
      child.kill()
      await exited
    }
  }
  try {
    child = spawn('dotnet', ['src/Novalist.Sync.Server/bin/Debug/net10.0/Novalist.Sync.Server.dll'],
      { cwd: source, env, windowsHide: true, stdio: 'ignore' })
    let launchError: Error | undefined
    child.on('error', error => { launchError = error })
    for (let attempt = 0; attempt < 100; attempt++) {
      if (launchError) throw launchError
      if (child.exitCode !== null) throw new Error('The collaboration test server exited during startup.')
      const response = await fetch(address + '/client-configuration').catch(() => null)
      if (response?.ok) return { address, close }
      await delay(100)
    }
    throw new Error('The collaboration test server did not become ready.')
  } catch (error) { await close(); throw error }
}
