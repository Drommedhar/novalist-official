import { spawn, type ChildProcessByStdio } from 'node:child_process'
import { app } from 'electron'
import { existsSync } from 'node:fs'
import { join } from 'node:path'
import type { Readable, Writable } from 'node:stream'
import type { MessagePortMain } from 'electron'

type BackendChild = ChildProcessByStdio<Writable, Readable, Readable>

/**
 * Spawns and supervises the Novalist.Backend process. stdout carries LSP-framed
 * JSON-RPC and is relayed byte-for-byte to the renderer's MessagePort; stderr is
 * mirrored to the main-process console for diagnostics.
 */
export class BackendProcess {
  private child: BackendChild | null = null
  private port: MessagePortMain | null = null
  private disposed = false

  start(): void {
    const exe = resolveBackendPath()
    const child = spawn(exe, [], { stdio: ['pipe', 'pipe', 'pipe'] })
    this.child = child

    child.on('error', (error) => {
      console.error(`[backend] failed to start ${exe}:`, error.message)
      this.child = null
    })

    child.stdout.on('data', (chunk: Buffer) => {
      this.port?.postMessage(chunk)
    })
    child.stderr.on('data', (chunk: Buffer) => {
      console.error('[backend]', chunk.toString().trimEnd())
    })
    child.on('exit', (code) => {
      console.error(`[backend] exited with code ${code}`)
      this.child = null
      if (!this.disposed) {
        // Supervise: restart and tell the renderer to re-attach and re-hydrate.
        this.start()
        this.port?.postMessage({ novalistControl: 'backend-restarted' })
      }
    })
  }

  /** Attaches the renderer-facing MessagePort and begins relaying frames. */
  attachPort(port: MessagePortMain): void {
    this.port?.close()
    this.port = port
    port.on('message', (event) => {
      const data = event.data as Uint8Array
      this.child?.stdin.write(Buffer.from(data))
    })
    port.start()
  }

  dispose(): void {
    this.disposed = true
    this.port?.close()
    this.port = null
    this.child?.kill()
    this.child = null
  }
}

function resolveBackendPath(): string {
  const override = process.env.NOVALIST_BACKEND_PATH
  if (override && existsSync(override)) return override
  if (app.isPackaged) {
    const name = process.platform === 'win32' ? 'Novalist.Backend.exe' : 'Novalist.Backend'
    return join(process.resourcesPath, 'backend', name)
  }
  // Dev: the debug build produced by `dotnet build` at the repo root.
  // __dirname is app/out/main, so the repo root is three levels up.
  const name = process.platform === 'win32' ? 'Novalist.Backend.exe' : 'Novalist.Backend'
  return join(__dirname, '..', '..', '..', 'Novalist.Backend', 'bin', 'Debug', 'net8.0', name)
}
