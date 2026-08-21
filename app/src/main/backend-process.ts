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
    // Unify the backend's data root with Electron's userData so settings and
    // extensions live in ONE place (macOS-native ~/Library/Application Support/
    // Novalist), instead of the .NET default (~/.config/Novalist). The e2e specs
    // set NOVALIST_SETTINGS_DIR explicitly, so respect it when already present —
    // and only allow the legacy migration when WE chose userData (i.e. no
    // external override), so tests never get legacy data copied into their dirs.
    const preset = process.env.NOVALIST_SETTINGS_DIR
    const env = {
      ...process.env,
      NOVALIST_SETTINGS_DIR: preset ?? app.getPath('userData'),
      ...(preset ? {} : { NOVALIST_ALLOW_LEGACY_MIGRATION: '1' }),
      // macOS only: the self-contained single-file backend extracts native libs
      // at startup. Its default target ($TMPDIR) is fine for the Developer ID
      // build, but under the Mac App Store sandbox it must live inside the app's
      // container. userData is always container-local, so point the extractor
      // there on darwin. Left unset on Windows/Linux to keep their default (and
      // avoid re-extracting into a non-temp dir every launch).
      ...(process.platform === 'darwin'
        ? { DOTNET_BUNDLE_EXTRACT_BASE_DIR: join(app.getPath('userData'), 'backend-cache') }
        : {}),
      // Extensions shipped inside the application. The backend copies them into
      // the writer's extensions folder on first run, because that folder is the
      // one it can write to - an installed extension keeps its settings, its
      // Python environment and its downloaded models beside itself, and the
      // application directory is read-only on macOS and unwritable for a
      // standard user on Windows.
      //
      // Both of these are off in the Mac App Store build: an extension is a
      // .NET assembly that arrives after review and adds features, which the
      // App Store does not allow an app to download and run. The flag stops the
      // loader before it discovers anything, and nothing is seeded for it to
      // find. See ExtensionLoader.ExtensionsDisabled.
      ...(isMasBuild() ? { NOVALIST_EXTENSIONS_DISABLED: '1' } : bundledExtensions())
    }
    const child = spawn(exe, [], { stdio: ['pipe', 'pipe', 'pipe'], env })
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

/**
 * True in the Mac App Store build, where the extension feature is off.
 *
 * NOVALIST_FORCE_MAS stands in for the real thing so the e2e run can render the
 * App Store build's UI without an App Store build, the same way
 * NOVALIST_FORCE_MOBILE stands in for the phone shell. It is never set in a
 * shipped build.
 */
function isMasBuild(): boolean {
  return (
    (process as NodeJS.Process & { mas?: boolean }).mas === true ||
    process.env.NOVALIST_FORCE_MAS === '1'
  )
}

/**
 * Where the extensions we ship live, when this build ships any.
 *
 * Absent in development, where extensions are deployed to the settings folder
 * by their own build - pointing this at a folder that does not exist would be
 * one more thing to explain in the log every launch.
 */
function bundledExtensions(): Record<string, string> {
  if (!app.isPackaged) return {}
  const bundled = join(process.resourcesPath, 'extensions')
  return existsSync(bundled) ? { NOVALIST_BUNDLED_EXTENSIONS: bundled } : {}
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
