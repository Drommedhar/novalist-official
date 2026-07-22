# Novalist Mobile - Phase 0 runbook (iOS)

Phase 0 of `docs/mobile-port-plan.md`: prove the seam. This is a spike, not a shipped
feature - the `Novalist.Mobile` project is deliberately kept out of `novalist-official.sln`
so the desktop build and the 100%-coverage gate (Core/Sdk/Backend) are untouched.

Two checkpoints:

1. **Seam proof** - the shared C# backend (`Novalist.Core` + the RPC facades) boots
   in-process on the device over a `FullDuplexStream` pair (no child process, no stdio)
   and answers `system/ping`. Shown on the first screen (`SeamPage`).
2. **Editor spike** (highest risk in the whole port) - the real contenteditable editor
   (`app/src/renderer/public/editor/editor.html`) loads in a HybridWebView on a real
   iPhone; validate typing, caret, and selection (`EditorSpikePage`).

## What's in the project

```
Novalist.Mobile/
  Novalist.Mobile.csproj      net10.0-ios, references Novalist.Core + Novalist.Backend
  MauiProgram.cs              MAUI bootstrap
  App.cs                      NavigationPage -> SeamPage
  Services/SeamProbe.cs       in-process BackendHost + system/ping round-trip
  Pages/SeamPage.cs           checkpoint 1 UI
  Pages/EditorSpikePage.cs    checkpoint 2 UI (HybridWebView loads editor.html)
  Platforms/iOS/              AppDelegate, Program, Info.plist
  Resources/                  placeholder app icon + splash
```

`editor.html` is pulled straight from the renderer as a `MauiAsset` (LogicalName
`editor\editor.html`) - single source of truth stays in `app/src/renderer/public/editor/`.

## One-time setup (needs your admin password - run these yourself)

Xcode.app is installed but not yet the active developer directory, and the MAUI iOS
workload is not installed. Both need `sudo`:

```sh
# Point the toolchain at the full Xcode (not Command Line Tools) and accept the license
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
sudo xcodebuild -license accept

# Install the MAUI iOS workload into the .NET 10 SDK
sudo dotnet workload install maui-ios
```

Verify:

```sh
xcodebuild -version            # should print Xcode, not the CommandLineTools error
dotnet workload list           # should list maui-ios / ios
```

## Build and run

Simulator (fastest smoke test of checkpoint 1):

```sh
dotnet build Novalist.Mobile/Novalist.Mobile.csproj -t:Run \
  -f net10.0-ios \
  -p:_DeviceName=:v2:udid=$(xcrun simctl list devices booted -j | \
     python3 -c "import sys,json;print(json.load(sys.stdin)['devices'] and next(iter([d['udid'] for v in json.load(sys.stdin)['devices'].values() for d in v if d['state']=='Booted']),''))")
```

If the one-liner is fiddly, just open the booted simulator first (`open -a Simulator`)
and run:

```sh
dotnet build Novalist.Mobile/Novalist.Mobile.csproj -t:Run -f net10.0-ios
```

Real device (needed for checkpoint 2 - the editor spike). This is the sequence that
actually worked on an iPhone 17 Pro Max (iOS 26.5, Xcode 26.6); substitute your own
UDID / signing identity:

Prerequisites (one-time):
- Enable Developer Mode on the phone: Settings > Privacy & Security > Developer Mode >
  on > restart > confirm the post-reboot prompt. The toggle only appears after the phone
  has been connected to a dev tool and rebooted.
- Sign into Xcode with your Apple ID (Xcode > Settings > Accounts), then
  Manage Certificates > "+" > Apple Development to mint the signing cert into the login
  keychain. Verify: `security find-certificate -c "Apple Development" -p | openssl x509
  -noout -subject` prints `OU=<TEAM_ID>` and `CN=Apple Development: <you> (...)`.
- A provisioning profile must exist for the bundle id. The .NET CLI does NOT generate one
  (it only consumes an existing profile), so let Xcode create it once: File > New >
  Project > iOS App with the SAME bundle id (`com.novalist.mobile`) and your team +
  "Automatically manage signing", then build it (Cmd-B) against the device. Xcode drops a
  wildcard `iOS Team Provisioning Profile: *` into
  `~/Library/Developer/Xcode/UserData/Provisioning Profiles/` (note: newer Xcode uses this
  path, not the legacy `~/Library/MobileDevice/Provisioning Profiles/`). The wildcard `*`
  covers `com.novalist.mobile` because the app declares no special entitlements.

Build, then install+launch (do NOT use `-t:Run` for device - a target-ordering bug throws
"The app must be built before ... mlaunch"; build and launch as two steps):

```sh
# 1. build + sign for arm64
dotnet build Novalist.Mobile/Novalist.Mobile.csproj -f net10.0-ios \
  -p:RuntimeIdentifier=ios-arm64 \
  -p:CodesignKey="Apple Development: <you> (<10-char cert id>)" \
  -p:CodesignProvision=Automatic

# 2. install + launch via devicectl (get UDID from `xcrun xctrace list devices`)
UDID=<your iPhone udid>
xcrun devicectl device install app --device "$UDID" \
  Novalist.Mobile/bin/Debug/net10.0-ios/ios-arm64/Novalist.Mobile.app
xcrun devicectl device process launch --device "$UDID" com.novalist.mobile
```

If iOS blocks the first launch: Settings > General > VPN & Device Management >
(your Apple ID) > Trust.

## What to look for

- **Checkpoint 1 pass:** SeamPage shows `Seam OK - backend <version>` in green.
  Failure shows the exception type + message in red - report that back.
- **Checkpoint 2 pass:** tap "Open editor spike", tap into the editor, and confirm:
  typing inserts at the caret; the caret is visible and lands where you tap; drag-select
  and the selection handles work; the on-screen keyboard does not cover the caret line
  oddly. Note anything janky - this is the go/no-go signal for the whole port.

## Phase 0 result (verified)

- Checkpoint 1 (seam): PASS on the iOS simulator AND on a real iPhone 17 Pro Max - the
  shared C# backend boots in-process over `FullDuplexStream` and answers `system/ping`.
- Checkpoint 2 (editor): PASS on the real device - the real `editor.html` loads in the
  HybridWebView (WKWebView) and typing, caret placement, tap-to-reposition, and
  drag-selection all behave like a native app. This was the single highest risk in the
  whole port; it is now retired. Go for Phase 1.
- HybridWebView loads `editor.html` from the `MauiAsset` `LogicalName` `editor\editor.html`
  under `HybridRoot="editor"` with no 404 - confirmed working, no `Resources/Raw` fallback
  needed.
- Toolchain that worked: .NET 10 SDK (10.0.300), MAUI 10.0.20 workload, Xcode 26.6.

## Phase 1 + 2 result (verified)

The real React renderer runs in the HybridWebView, talks to the in-process backend over a
transparent JSON-RPC byte bridge, AND uses native `window.novalist` capabilities. Verified
on the iOS simulator end to end: create a project (native pickFolder -> app-container dir,
then project/create over RPC) and the full project workspace + dashboard load.
`rpc/client.ts` is UNCHANGED.

IMPORTANT correction: an earlier note here claimed Phase 1 transport was "verified" from the
welcome screen alone. That was wrong - the welcome screen renders identically whether RPC
resolves or hangs (empty recents either way). The transport was in fact NOT working until the
Phase 2 fix below, because the page never loaded MAUI's JS bridge library.

### The critical gotcha: load `_framework/hybridwebview.js`

On .NET 10 MAUI, HybridWebView serves its JS bridge library at `_framework/hybridwebview.js`
but does NOT auto-inject it. Without a `<script src="_framework/hybridwebview.js">` in the
page, `window.HybridWebView` is undefined, `SendRawMessage` silently no-ops, and every
JS->native call (RPC frames AND host calls) hangs forever with no error. index.mobile.html
now includes it in `<head>` before the shim. (native->JS via EvaluateJavaScript works without
the library, which is why the app still booted - masking the broken JS->native half.)

How it fits together:

- `app/vite.mobile.config.ts` + `npm run build:mobile` builds a plain-web (non-Electron)
  bundle from `app/src/renderer` into `Novalist.Mobile/Resources/Raw/app` (git-ignored).
  Entry `app/src/renderer/index.mobile.html`.
- `app/src/renderer/src/mobile/shim.ts` installs `window.novalist` for mobile. Two channels
  share the one HybridWebView raw-message pipe, disjoint by the first byte of each JS->native
  message:
  - RPC transport: `requestBackendPort()` mirrors the Electron preload - it creates a
    `MessageChannel`, hands port1 to the page (so `rpc/client.ts` sees a normal MessagePort
    and needs no change), and pumps port2's LSP-framed bytes as base64 (never starts '{').
  - Host bridge (Phase 2): the native window.novalist methods (pickFolder, pickFile, saveFile,
    openExternal, copyText, ...) send JSON `{id,method,args}` (starts '{') and await a keyed
    promise resolved by `window.__novalistHostResult`.
- `Novalist.Mobile/Pages/RendererHostPage.cs` hosts the bundle (`HybridRoot="app"`,
  `DefaultFile="index.mobile.html"`) and: pipes RPC bytes to `BackendHost` over a
  `FullDuplexStream` pair, and dispatches host calls via MAUI Essentials (Phase 2:
  pickFolder -> app-container `Projects` dir; pickFile -> FilePicker; openExternal -> Launcher;
  copyText -> Clipboard). JS->native via `RawMessageReceived`; native->JS via
  `EvaluateJavaScript("window.__novalistRecv(...)" / "...HostResult(...)")`. App root is this page.
- The bundle is included via an explicit `MauiAsset` glob in the csproj (the implicit
  `Resources/Raw` glob misses the freshly-generated `app/` folder due to evaluation-time
  caching).
- CSP note: index.mobile.html's CSP allows `script-src 'self' 'unsafe-eval'` - the served
  bridge library is same-origin ('self'); 'unsafe-eval' covers the HybridWebView invoke paths.

Phase 2 scope decision (product owner): app-container storage first. `pickFolder` returns a
writable sandbox dir (`FileSystem.AppDataDirectory/Projects`) so create/open/edit/save work
immediately. External folders (iOS security-scoped document-picker URLs + persistent bookmarks;
Android SAF tree URIs) via the `beginProjectAccess`/`endProjectAccess` seam are a later addition;
those methods are currently no-ops returning "accessible".

### Fast renderer iteration (skip the multi-minute MAUI build)

A MAUI/iOS build relinks + repackages the whole app every time, so DO NOT `dotnet build`
for renderer/TS changes. Instead, after the app is installed once on a booted simulator:

```sh
./Novalist.Mobile/dev-reload-sim.sh   # ~2s: build:mobile + hot-swap bundle + relaunch
```

Only a change to the C# bridge (`Novalist.Mobile/*.cs`) needs a real
`dotnet build ... -f net10.0-ios` + reinstall.

## Phase 3 result (verified)

Backend portability stubs + Git UI hidden on mobile. Verified on the simulator: the app
works, no Git activity-bar entry, no status-bar Git indicator, no errors.

- `Novalist.Core/Services/IProcessRunner.cs` gains `UnavailableProcessRunner` - every run
  reports a non-zero exit with no output, so shell-out services degrade to "unavailable"
  instead of throwing. Unit-tested (both overloads) to hold Core at 100% coverage.
- `GitRpc` now threads its injected `IProcessRunner` into its internal `GitService` (it
  previously hardcoded a real `ProcessRunner` there), so one runner disables Git on both
  paths. `BackendHost(settingsDir, IProcessRunner?)` passes it through; the mobile host
  (`RendererHostPage`) injects `UnavailableProcessRunner`. Result: `git/installed` -> false,
  `git/status` -> null. Covered by a GitRpc test; GitRpc + BackendHost stay line-rate 1.
- Renderer: `window.novalist.isMobile` (set by the mobile shim) drives `ActivityBar` to hide
  the `git` view. The status-bar Git indicator already self-hides when `git/status` is null.
- UpdateService and LinuxDependencyService are NOT reachable from the in-process backend (no
  RPC facade calls them; Electron main drives updates on desktop), so nothing to neutralize
  there on mobile. Left as-is; documented here so it isn't re-investigated.

Known limitation surfaced during Phase 3 testing: recents store ABSOLUTE paths, but iOS
changes the app's Data-container UUID on every reinstall, so a recent project from a prior
install fails to reopen ("No Novalist project found"). Fix later by storing app-container
project paths relative to `FileSystem.AppDataDirectory` (ties into the deferred external-folder
work). Does not affect projects created + used within one install.

## Phase 4 result (verified, in progress)

Responsive single-pane mobile layout with a NATIVE iOS 26 Liquid Glass bottom tab bar.
Verified on the simulator: welcome (no tab bar) -> project (tab bar appears) -> switch tabs ->
Manuscript binder (full-width) -> tap scene -> full-screen editor + back.

- Navigation is a native UIKit `UITabBar` (adopts iOS 26 Liquid Glass automatically) overlaid
  on the HybridWebView by `RendererHostPage` (`#if IOS`), NOT an HTML bar - true system glass.
  Tabs: Dashboard, Manuscript, Codex, Search, More. On select it calls
  `EvaluateJavaScript(window.__novalistTab('<key>'))`. Hidden until a project is open, toggled
  by `window.novalist.setNavVisible(bool)` (a host-bridge call driven by AppShell's isLoaded).
- Web side: `AppShell` renders `<MobileShell>` instead of the desktop multi-pane when
  `window.novalist.isMobile`. `MobileShell` shows one full-screen view per tab (DashboardView,
  CodexView, SettingsView for "More"; Manuscript = Binder full-width, or EditorFrame + a back
  bar when a scene is open). Search opens the Find/Replace dialog. Desktop Toolbar/StatusBar and
  the pane rails are hidden on mobile; content insets its bottom by `--nl-mobile-tabbar-h` and
  respects the safe areas (viewport-fit=cover).
- macOS already has native Liquid Glass (Electron `app/src/main/glass.ts` -> NSGlassEffectView
  on macOS 26+); nothing to add there.

Still open in Phase 4: iPad adaptive multi-pane (device idiom); feature-flag the remaining
non-v1 views into the More sheet (only Git is hidden so far); native<-web tab-selection sync
when the web changes tab itself (native taps already sync); editor input hardening polish.

## Next / deferred

- Deferred storage: external-folder access (iOS security-scoped URLs + bookmarks, Android SAF)
  behind beginProjectAccess/endProjectAccess, AND relative recents paths (see limitation above).
- Not yet done: redeploy Phases 1-4 to a physical device (only the Phase 0 spike ran on the
  real iPhone).
