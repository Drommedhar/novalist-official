# Mobile collaboration

iPhone and iPad use the existing relay and direct TCP protocols. Steam is excluded
from the mobile backend and connection menu. The relay retains no projects; hosting
requires a running app. iOS suspension interrupts networking, so prefer a desktop
host for long sessions and keep the mobile app in the foreground while editing.

The MAUI host supplies Keychain credentials and `WebAuthenticator` sign-in. Register
`com.novalist.app:/oauth/callback` on the provider's public/native client, with
authorization-code flow and PKCE S256. Keep the desktop loopback redirects. The
relay's existing `/client-configuration` response is shared by both clients.

The document bridge runs the same Yjs document engine in WKWebView that desktop
runs in its document process. The mobile transport also uses the existing workspace
coordinator: saves finish and editing pauses before initial synchronization.
New local copies default to the app's Documents folder, visible in Files; the
existing Files picker remains available for another destination or existing copy.

## Build on a Mac

Build the renderer with `npm ci` and `npm run build:mobile` in `app`, then run:

```sh
dotnet build Novalist.Mobile/Novalist.Mobile.csproj -r iossimulator-arm64
```

Use the Xcode and MAUI iOS versions required by `Novalist.Mobile.csproj`. The native
libsodium NuGet package supplies the device archive. Simulator builds compile the
same pinned 1.0.22 release from its SHA-256-verified upstream archive; Xcode, curl
and make are required. The output is cached under `obj`, with compiler output in
`sodium/<runtime>/build.log`. No native archive is committed.

## Integration checks

Run `node --test scripts/mobile-collaboration.test.mjs scripts/mobile-workspace.test.mjs
scripts/mobile-shim.test.mjs scripts/collaboration-documents.test.mjs` in `app`.
`HostedMobileSignInTests` checks real OIDC discovery, PKCE, signatures and state
validation using the custom mobile callback. `MobileDocumentRequestsTests` checks
native request cancellation and reply ownership without UIKit.

`tests/Novalist.Mobile.Smoke` is an opt-in simulator harness. Build with
`-p:MobileSmokeTest=true`; it refuses Release and device builds. It replaces the
application entry point only for that test build, while loading the production
renderer, native bridge and backend. Normal builds do not include the harness.

`app/e2e/fixtures/mobileSmokeHost.ts` launches an isolated desktop project, writes
an invitation/configuration under `app/out/mobile-smoke`, waits for a mobile editor
change, and sends a desktop edit back. Bundle it with esbuild (Node ESM, external
packages) and run it from `app`. Set `NOVALIST_MOBILE_SMOKE_MODE=hosted` to use the
local community-server fixture instead of TCP. The hosted test requires its existing
checkout/build under `app/out/collaboration-sync-server`.

Install the test app on a fresh simulator. Prefix `guest.js` with
`window.mobileSmokeConfig = <config.json contents>;` and copy it to the app's Library
directory as `collaboration-smoke.js`. For the hosted test, also copy the generated
`credentials.json` as `collaboration-credentials.json`. These are disposable local
test-provider credentials, never real accounts. Generate fresh credentials before
repeating a hosted run because refresh tokens are single-use.

Forward test ports when desktop and simulator run on different machines: 42682 for
TCP; 42683 and 42684 for the local identity provider and relay. Launch the app with
`simctl`; results and progress are written beside the script. The check uses the
collaboration menu and existing editor, verifies both directions and formatting,
and confirms the local copy survives leaving. The hosted smoke check begins with
a signed-in Keychain account; it does not automate the system browser's consent UI.
Test that browser flow with the configured provider on an iPhone or iPad before release.

For the reverse direction, place `host.js` in the simulator instead. Copy its
result to `app/out/mobile-smoke/mobile-host.json`, forward local port 42685 to
the Mac, and run the bundled `mobileSmokeGuest.ts` fixture from `app`. It checks
that a desktop can join the mobile host and edit its project.

## Validation on 2026-10-23

Native iPhone and iPad simulator runs exchanged edits with Windows through direct
TCP and the community relay. They preserved formatting and local copies; the
iPad run also verified local saving after leaving. A Windows client joined an
iPhone-hosted session and edited its project. Document, workspace, sign-in and
reconnect tests passed, along with the desktop two-writer regression check.

The unsigned `ios-arm64` Release build passed with `-p:MtouchUseLlvm=false`.
The default LLVM optimizer failed while compiling `aot-instances.dll` on the test
Mac; its cause remains unresolved. This validation override does not change the
project's release defaults. Verify the normal optimized build and system-browser
sign-in on a physical device before release.
