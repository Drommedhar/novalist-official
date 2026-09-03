import { spawn } from 'node:child_process'
import { randomUUID } from 'node:crypto'
import { chmodSync, unlinkSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'

/** Quotes one value as a literal POSIX-shell word. */
function bashQuote(value: string): string {
  return `'${value.replaceAll("'", `'\\''`)}'`
}

/**
 * Builds the detached handoff used by an AppImage update.
 *
 * The new AppImage cannot start while this process owns Electron's
 * single-instance lock. The helper therefore waits for this PID to disappear,
 * replaces the current AppImage when it can, and only then launches the update.
 */
export function buildLinuxUpdateScript(
  downloadedAppImage: string,
  currentAppImage: string,
  runningPid: number
): string {
  const downloaded = bashQuote(downloadedAppImage)
  const installed = bashQuote(currentAppImage)
  const status = bashQuote(`${downloadedAppImage}.handoff.log`)

  return (
    '#!/bin/bash\n' +
    'set -u\n' +
    `DOWNLOADED=${downloaded}\n` +
    `INSTALLED=${installed}\n` +
    `STATUS=${status}\n` +
    'BACKUP="${INSTALLED}.novalist-previous"\n' +
    'STAGED="${INSTALLED}.novalist-update.$$"\n' +
    'trap \'rm -f -- "$0" "$STAGED"\' EXIT\n' +
    'write_status() { printf \'%s\\n\' "$1" > "$STATUS" 2>/dev/null || true; }\n' +
    'launch_target() {\n' +
    '  if command -v setsid >/dev/null 2>&1; then\n' +
    '    setsid nohup "$1" </dev/null >/dev/null 2>&1 &\n' +
    '  else\n' +
    '    nohup "$1" </dev/null >/dev/null 2>&1 &\n' +
    '  fi\n' +
    '  LAUNCHED_PID=$!\n' +
    '  sleep 1\n' +
    '  kill -0 "$LAUNCHED_PID" 2>/dev/null\n' +
    '}\n' +
    'restore_previous() {\n' +
    '  mv -f -- "$BACKUP" "$INSTALLED" 2>/dev/null && chmod +x -- "$INSTALLED"\n' +
    '}\n' +
    `while kill -0 ${runningPid} 2>/dev/null; do sleep 0.2; done\n` +
    'sleep 0.5\n' +
    'if ! chmod +x -- "$DOWNLOADED"; then\n' +
    '  write_status "stage=chmod-new result=failed"\n' +
    '  launch_target "$INSTALLED" || true\n' +
    '  exit 1\n' +
    'fi\n' +
    // Copy onto the installed filesystem before touching the live path. The
    // final rename is then same-filesystem and atomic instead of a cross-device
    // mv that can remove the installed AppImage before its copy fails.
    'if ! cp -fp -- "$DOWNLOADED" "$STAGED" || ! cmp -s -- "$DOWNLOADED" "$STAGED"; then\n' +
    '  write_status "stage=stage-copy result=failed"\n' +
    '  launch_target "$INSTALLED" || true\n' +
    '  exit 1\n' +
    'fi\n' +
    'if ! chmod +x -- "$STAGED"; then\n' +
    '  write_status "stage=chmod-staged result=failed"\n' +
    '  launch_target "$INSTALLED" || true\n' +
    '  exit 1\n' +
    'fi\n' +
    'if ! cp -fp -- "$INSTALLED" "$BACKUP" || ! cmp -s -- "$INSTALLED" "$BACKUP"; then\n' +
    '  write_status "stage=backup result=failed"\n' +
    '  launch_target "$INSTALLED" || true\n' +
    '  exit 1\n' +
    'fi\n' +
    'if ! mv -f -- "$STAGED" "$INSTALLED"; then\n' +
    '  if restore_previous; then\n' +
    '    if launch_target "$INSTALLED"; then\n' +
    '      write_status "stage=replace result=rolled-back"\n' +
    '    else\n' +
    '      write_status "stage=replace result=rollback-relaunch-failed"\n' +
    '    fi\n' +
    '  else\n' +
    '    write_status "stage=replace result=restore-failed backup=$BACKUP"\n' +
    '    launch_target "$DOWNLOADED" || true\n' +
    '  fi\n' +
    '  exit 1\n' +
    'fi\n' +
    'if ! chmod +x -- "$INSTALLED"; then\n' +
    '  if restore_previous; then\n' +
    '    if launch_target "$INSTALLED"; then\n' +
    '      write_status "stage=chmod-installed result=rolled-back"\n' +
    '    else\n' +
    '      write_status "stage=chmod-installed result=rollback-relaunch-failed"\n' +
    '    fi\n' +
    '  else\n' +
    '    write_status "stage=chmod-installed result=restore-failed backup=$BACKUP"\n' +
    '    launch_target "$DOWNLOADED" || true\n' +
    '  fi\n' +
    '  exit 1\n' +
    'fi\n' +
    'if launch_target "$INSTALLED"; then\n' +
    '  rm -f -- "$DOWNLOADED"\n' +
    '  write_status "stage=relaunch result=ok backup=$BACKUP"\n' +
    '  exit 0\n' +
    'fi\n' +
    'if restore_previous; then\n' +
    '  if launch_target "$INSTALLED"; then\n' +
    '    write_status "stage=relaunch result=rolled-back"\n' +
    '  else\n' +
    '    write_status "stage=relaunch result=rollback-relaunch-failed"\n' +
    '  fi\n' +
    'else\n' +
    '  write_status "stage=relaunch result=restore-failed backup=$BACKUP"\n' +
    '  launch_target "$DOWNLOADED" || true\n' +
    'fi\n' +
    'exit 1\n'
  )
}

export function makeLinuxAppImageExecutable(path: string): void {
  // Fail while Novalist is still open. Once the old process exits, the helper
  // can only recover to the backup; it cannot explain a bad download in UI.
  chmodSync(path, 0o755)
}

/** Stages and starts the updater helper, resolving only once bash was spawned. */
export async function stageLinuxAppImageUpdate(
  downloadedAppImage: string,
  currentAppImage: string,
  tempDirectory: string,
  runningPid: number
): Promise<string> {
  makeLinuxAppImageExecutable(downloadedAppImage)
  const scriptPath = join(tempDirectory, `novalist-update-${randomUUID()}.sh`)
  writeFileSync(
    scriptPath,
    buildLinuxUpdateScript(downloadedAppImage, currentAppImage, runningPid),
    { encoding: 'utf8', mode: 0o700 }
  )
  makeLinuxAppImageExecutable(scriptPath)

  try {
    await new Promise<void>((resolve, reject) => {
      const child = spawn('/bin/bash', [scriptPath], {
        detached: true,
        stdio: 'ignore',
        windowsHide: true
      })
      child.once('error', reject)
      child.once('spawn', () => {
        child.unref()
        resolve()
      })
    })
  } catch (error) {
    try {
      unlinkSync(scriptPath)
    } catch {
      // Best effort: a stale helper in the temporary directory is harmless.
    }
    throw error
  }

  return scriptPath
}
