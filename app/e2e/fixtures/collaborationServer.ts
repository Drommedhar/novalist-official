import { spawn, spawnSync, type ChildProcess } from 'node:child_process'
import { once } from 'node:events'
import { readFileSync } from 'node:fs'
import { createServer } from 'node:net'
import { resolve } from 'node:path'
import { randomUUID } from 'node:crypto'
import { setTimeout as delay } from 'node:timers/promises'

export async function collaborationServer(authority: string): Promise<{ address: string; close(): Promise<void> }> {
  const source = resolve('out/collaboration-sync-server')
  const binaries = resolve('out/collaboration-postgres/pgsql/bin')
  const port = readFileSync(resolve('out/collaboration-postgres-port.txt'), 'utf8').trim()
  const database = 'novalist_desktop_test_' + randomUUID().replaceAll('-', '')
  const reservation = createServer()
  reservation.listen(0, '127.0.0.1')
  await once(reservation, 'listening')
  const endpoint = reservation.address()
  if (!endpoint || typeof endpoint === 'string') throw new Error('The test server has no port.')
  const address = `http://127.0.0.1:${endpoint.port}`
  await new Promise<void>((done, reject) => reservation.close(error => error ? reject(error) : done()))
  const env = {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: address,
    ConnectionStrings__Default: `Host=127.0.0.1;Port=${port};Database=${database};Username=novalist_test`,
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
    const dropped = spawnSync(resolve(binaries, 'dropdb.exe'), ['--host=127.0.0.1', '--port=' + port,
      '--username=novalist_test', '--if-exists', '--force', database], { windowsHide: true, encoding: 'utf8' })
    if (dropped.status !== 0) throw new Error('Failed to remove the isolated collaboration test database: ' + dropped.stderr)
  }
  try {
    const migrated = spawnSync(resolve('out/collaboration-tools/dotnet-ef.exe'), ['database', 'update', '--no-build',
      '--project', 'src/Novalist.Sync.Server/Novalist.Sync.Server.csproj'], { cwd: source, env, windowsHide: true, encoding: 'utf8' })
    if (migrated.status !== 0) throw new Error('Failed to migrate the collaboration test database: ' + migrated.stdout + migrated.stderr)
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
