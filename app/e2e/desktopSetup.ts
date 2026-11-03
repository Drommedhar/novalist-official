/** Native Electron tests must not take over an unsuspecting developer's desktop. */
export default function requireDesktopPermission(): void {
  if (process.env.NOVALIST_E2E_ALLOW_DESKTOP === '1') return
  throw new Error(
    'Electron E2E tests activate real windows and may enter fullscreen. ' +
    'Run them on the isolated GitHub Actions runners or a dedicated desktop session. ' +
    'Only with permission to use that desktop, set NOVALIST_E2E_ALLOW_DESKTOP=1. ' +
    'Use --list to inspect tests without opening windows.'
  )
}
