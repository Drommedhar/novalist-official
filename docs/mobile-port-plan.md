# Novalist Mobile Port Plan (iOS, iPadOS, Android)

Status: proposed, not started. This is a self-contained handoff document. It targets a fresh
agent picking the work up (likely on macOS, which is required for iOS/iPadOS builds and Apple
signing). Android builds work from any OS but the shared MAUI toolchain is easiest on macOS.

Goal: ship native mobile apps for iOS, iPadOS, and Android with the least possible change to the
existing codebase. Novalist is currently Electron (renderer) + a spawned .NET 8 backend process
communicating over stdio JSON-RPC.

---

## 1. Why this is cheap: the existing seam

Novalist's Electron architecture already isolates the two halves that matter:

- The React renderer never touches the OS or the backend directly. Its only outside contacts are:
  1. JSON-RPC over an abstract `MessagePort` (`app/src/renderer/src/rpc/client.ts`), and
  2. the `window.novalist` preload bridge (~20 call sites: file pickers, clipboard,
     project-root access, self-update, platform cosmetics).
  There are zero `fs` / `path` / `electron` / `require` imports anywhere in `app/src/renderer`.
- The backend is portable .NET 8 (`Novalist.Core`, `Novalist.Sdk`, `Novalist.Backend`), all
  `net8.0`, with no P/Invoke, WPF, WinForms, System.Drawing, or registry. Storage is pure
  filesystem JSON + Markdown behind `IFileService`. All 26 RPC facades (~246 methods) hang off a
  single injected `Workspace`.
- The transport is already abstracted. The backend speaks LSP-framed JSON-RPC over a stream; the
  backend tests already drive `BackendHost` in-process via `FullDuplexStream` instead of stdio.

So the port keeps both halves and replaces only the Electron shell in the middle. The renderer and
the C# core are shared, unchanged, across all three mobile platforms.

---

## 2. Decisions already made (do not re-litigate)

These were confirmed with the product owner before this plan was written:

1. Git: stub it on mobile. `GitService` / `IProcessRunner` shell out to the `git` binary via
   `Process.Start`, which no mobile sandbox allows. Make Git a no-op / "unavailable" capability on
   mobile and hide the versioning UI behind a capability flag. Filesystem snapshots and word-history
   do not shell out and stay working.
2. Storage: app-container by default, plus external folders via the OS document picker
   (iOS/iPadOS security-scoped document-picker URLs; Android Storage Access Framework tree URIs).
   Fully offline. No remote backend.
3. v1 scope: read + write core. Manuscript editing, scenes/chapters, Codex, dashboard, search.
   Defer extensions webviews, maps / 3D, timeline, plot grid, and export-heavy views (feature-flag
   them off on mobile for v1).

---

## 3. Recommended architecture: one MAUI shell for all three platforms

iOS and iPadOS share a single .NET target (`net8.0-ios` / MAUI iOS head) and differ only in adaptive
layout (device idiom). Android is a separate runtime (`net*-android`, Android System WebView). To
avoid writing two or three separate native shells, host all platforms through one .NET MAUI project:

```
┌───────────────── .NET MAUI shell (one project, replaces Electron main + preload) ────────────────┐
│  Targets: net9.0-ios (iPhone + iPad), net9.0-android   [optional: net9.0-maccatalyst]            │
│                                                                                                  │
│  MAUI HybridWebView  ── loads the built Vite web bundle (the entire React renderer)              │
│      │   two-way JS bridge (HybridWebView RawMessage / InvokeJavaScript)                          │
│      │        - shuttles JSON-RPC bytes         (replaces the Electron MessagePort)              │
│      │        - implements window.novalist via MAUI Essentials + small platform code            │
│      ▼                                                                                            │
│  In-process JSON-RPC over FullDuplexStream   (no stdio, no child process)                        │
│      ▼                                                                                            │
│  Novalist.Core + the 26 Rpc/*.cs facades + Workspace     <- reused unchanged                     │
│      ▼                                                                                            │
│  Platform filesystem: app data dir + document-picker / SAF external folders                      │
└──────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Why MAUI HybridWebView instead of hand-rolled WKWebView + Android WebView shells:

- `HybridWebView` (.NET 9 MAUI) is purpose-built to host an SPA web bundle with a two-way JS
  bridge, and it abstracts the WKWebView (Apple) vs android.webkit.WebView (Android) difference.
  One bridge implementation instead of two.
- MAUI Essentials abstracts most of `window.novalist` cross-platform in one implementation:
  `FilePicker`, `FolderPicker` (.NET 9), `Clipboard`, `Launcher`, `FileSystem.AppDataDirectory`.
- iPadOS is handled inside the iOS target via device-idiom detection driving adaptive layout; no
  separate project.

Note on framework versions: keep `Novalist.Core` / `Novalist.Backend` at `net8.0` (do not disturb
the 100%-coverage CI). The MAUI shell needs `net9.0` for HybridWebView and references the `net8.0`
libraries directly (net9 app referencing net8 libs is supported). If the repo has moved to a newer
LTS by the time this starts, use that instead but keep the shell one band ahead of, or equal to,
the core.

Alternative considered and rejected for v1: separate native shells (.NET for iOS + .NET for
Android). More per-platform bridge code, no offsetting benefit at this scope. Revisit only if
HybridWebView proves too limiting for the editor.

---

## 4. Platform matrix

| Concern                    | iOS / iPadOS                                   | Android                                       |
|----------------------------|------------------------------------------------|-----------------------------------------------|
| WebView                    | WKWebView (via HybridWebView)                  | Android System WebView / Chromium (via same)  |
| Child processes            | Forbidden -> backend in-process                | Discouraged/limited -> backend in-process     |
| File pickers               | UIDocumentPicker (via MAUI FilePicker/Folder)  | Storage Access Framework (same MAUI API)      |
| External folder access     | Security-scoped document-picker URLs           | SAF persistable tree URI permissions          |
| App data root              | App container (FileSystem.AppDataDirectory)    | App-specific scoped storage (same API)        |
| Clipboard / open external  | UIPasteboard / OpenUrl (MAUI Clipboard/Launcher)| ClipboardManager / Intent (same MAUI API)    |
| Self-update                | No-op (App Store)                              | No-op (Play Store)                            |
| iPad-only                  | Adaptive multi-pane, external keyboard, Pencil | n/a                                           |
| Editor hardening surface   | Mobile Safari contenteditable quirks           | Chromium WebView contenteditable quirks       |

Both WebViews are modern but not identical; the contenteditable editor must be validated on both.

---

## 5. What ships unchanged (the reuse win)

- All of `Novalist.Core` (services, models, filesystem layout, migrations, snapshots, export,
  grammar, search) except the Git/update/linux stubs in section 7.
- All 26 RPC facades and `Workspace` (`Novalist.Backend/Rpc/*.cs`, `Novalist.Backend/Workspace.cs`).
- The entire React renderer: zustand stores, views, i18n, RPC client, the contenteditable iframe
  editor. Reused with responsive layout + capability flags layered on, not rewritten.

Reuse estimate:

| Layer                                   | Reuse     | New / changed                                             |
|-----------------------------------------|-----------|----------------------------------------------------------|
| Novalist.Core + RPC facades             | ~100%     | Git / update / linux stubs only                          |
| Transport                               | pattern exists | swap stdio -> FullDuplexStream; renderer port acquisition |
| Renderer app / stores / i18n            | ~100%     | responsive layout + mobile capability flags              |
| Editor (contenteditable iframe)         | portable  | mobile input hardening (both WebViews)                   |
| Electron main / preload                 | 0%        | replaced by one MAUI shell + bridge (~20 methods)        |

---

## 6. Native capability mapping (`window.novalist` -> mobile)

The ~20 preload methods (currently in `app/src/preload/index.ts`) map as follows. Most are one
cross-platform MAUI Essentials call:

| window.novalist method(s)                         | Mobile implementation                                  |
|---------------------------------------------------|--------------------------------------------------------|
| pickFolder                                        | MAUI FolderPicker (.NET 9)                             |
| pickFile / saveFile                               | MAUI FilePicker / FileSaver                            |
| setProjectRoot / beginProjectAccess / endProjectAccess | persist security-scoped URL (iOS) / SAF tree URI (Android); mirrors the existing macOS bookmark seam in projectStore.ts |
| copyText / readClipboardImage                     | MAUI Clipboard                                          |
| openExternal                                      | MAUI Launcher.OpenAsync                                 |
| revealPath                                        | share sheet / no-op                                    |
| requestBackendPort                                | resolve to the HybridWebView JS bridge (see phase 1)   |
| checkAppUpdate / downloadAppUpdate / autoUpdate   | no-op (store-delivered updates; reuse isMas gating)    |
| platform / material / isMas                       | mobile-appropriate values                              |

---

## 7. Backend portability stubs

- Git: on mobile, make `Novalist.Core/Services/GitService.cs` and `IProcessRunner.cs` no-op /
  return "unavailable" so `Novalist.Backend/Rpc/GitRpc.cs` degrades gracefully. Guard the
  renderer's Git/versioning UI behind a capability flag so it hides on mobile.
- Neutralize `Novalist.Core/Services/UpdateService.cs` and `LinuxDependencyService.cs` on mobile
  (both use `Process.Start`). UpdateService is already `[ExcludeFromCodeCoverage]`.
- Do these behind the existing service interfaces (inject a mobile implementation), not with
  `#if` scattered through logic, to keep the 100%-coverage gate satisfied with unit tests.

---

## 8. Phased implementation

Each phase should end with a working, demonstrable checkpoint and sign-off before the next.

### Phase 0 - Prove the seam (spike)
- New MAUI project `Novalist.Mobile/` (net9.0-ios;net9.0-android) referencing `Novalist.Core` and
  the `Novalist.Backend` facades.
- New in-process host entry beside `Novalist.Backend/BackendHost.cs` that runs the same
  `AddLocalRpcTarget` wiring over a `FullDuplexStream` (the pattern already used in backend tests)
  instead of the `Console` stdio in `Novalist.Backend/Program.cs`. No child process.
- Confirm `system/ping` round-trips headlessly on both an iOS simulator and an Android emulator.
- Highest-risk spike to run in parallel here: load the contenteditable editor
  (`app/src/renderer/public/editor/editor.html`) in a bare HybridWebView on a real iPhone and a
  real Android device and validate typing, caret, and selection. This is the single biggest unknown;
  do not commit to the full plan until it passes.

### Phase 1 - WebView shell + transport bridge
- Add a plain-web build target that produces the renderer bundle without Electron assumptions
  (parallel to `app/electron.vite.config.ts`). Output a static bundle the HybridWebView loads.
- Bridge JSON-RPC bytes between JS and the in-process backend using HybridWebView's raw-message
  channel (native <-> JS).
- In `app/src/renderer/src/rpc/client.ts`, make `requestBackendPort()` resolve to the HybridWebView
  bridge when running on mobile, keeping the Electron MessagePort path intact. This is the only
  renderer transport change.
- Server-pushed `ui/*` notifications already flow through
  `app/src/renderer/src/stores/hostBridgeStore.ts`; verify they arrive over the new transport.

### Phase 2 - Native window.novalist bridge
- Implement the section 6 mapping using MAUI Essentials plus small platform-conditional code for
  security-scoped URLs (iOS/iPadOS) vs SAF persistable tree URIs (Android).
- Default project root to the app data directory; external folders via the picker.
- Stub self-update to no-ops; set platform/material/isMas.

### Phase 3 - Backend portability stubs
- Implement section 7 (Git, update, linux) as mobile service implementations behind existing
  interfaces. Add unit tests to keep Core/Sdk/Backend at 100% line coverage.
- Add a capability flag surfaced to the renderer so Git/versioning UI hides on mobile.

### Phase 4 - Mobile renderer (read + write core scope)
- Responsive shell: collapse the desktop activity bar (`app/src/renderer/src/stores/shellStore.ts`
  activityGroups + `ActivityBar.tsx`) and multi-pane layout into a phone navigation; on iPad, use an
  adaptive multi-pane layout via device idiom.
- Scope v1 to manuscript editing, scenes/chapters, Codex, dashboard, search. Feature-flag off
  extensions webviews, maps / 3D, timeline, plot grid, and export-heavy views on mobile.
- Editor hardening: the contenteditable + execCommand editor
  (`app/src/renderer/public/editor/editor.html`, `manuscript-editor.html`) needs mobile touch
  selection, virtual-keyboard behavior, and execCommand fallbacks on both WKWebView and Android
  WebView.

### Phase 5 - Packaging and store
- iOS/iPadOS: MAUI iOS AOT build, App Store provisioning and signing (macOS required),
  offline-data policy, verify on device.
- Android: signed AAB, Play Store listing, scoped-storage compliance, verify on device.
- Confirm fully offline operation on all three.

---

## 9. Key files (orientation for the implementing agent)

- Transport / host: `Novalist.Backend/Program.cs`, `Novalist.Backend/BackendHost.cs`,
  `Novalist.Backend/Workspace.cs`, and the Electron side being replaced,
  `app/src/main/backend-process.ts`, `app/src/main/index.ts`, `app/src/preload/index.ts`.
- Renderer RPC: `app/src/renderer/src/rpc/client.ts`, `app/src/renderer/src/rpc/contract.ts`.
- Renderer host bridge / notifications: `app/src/renderer/src/stores/hostBridgeStore.ts`.
- Project-root / sandbox seam to mirror: `app/src/renderer/src/stores/projectStore.ts`.
- Shell / navigation to make responsive: `app/src/renderer/src/stores/shellStore.ts`,
  `ActivityBar.tsx`, `MainArea.tsx`.
- Editor: `app/src/renderer/public/editor/editor.html`,
  `app/src/renderer/public/editor/manuscript-editor.html`,
  `app/src/renderer/src/views/editor/editorBridge.ts`, `EditorFrame.tsx`.
- Storage / model: `Novalist.Core/Services/ProjectService.cs`,
  `Novalist.Core/Services/IFileService.cs` + `FileService.cs`,
  `Novalist.Core/Services/FilesystemMigrator.cs`, `Novalist.Core/Models/*.cs`.
- Portability hazards to stub: `Novalist.Core/Services/GitService.cs`,
  `Novalist.Core/Services/IProcessRunner.cs`, `Novalist.Core/Services/UpdateService.cs`,
  `Novalist.Core/Services/LinuxDependencyService.cs`.
- Build config: `app/electron.vite.config.ts`, `Directory.Build.props`.

---

## 10. Risks and unknowns

1. Editor on mobile WebViews (highest). contenteditable + execCommand behaves differently on mobile
   Safari and Android Chromium (touch selection, virtual keyboard, execCommand not guaranteed).
   Validate on real devices in Phase 0 before committing.
2. HybridWebView maturity. Confirm it hosts a large SPA + high-frequency JSON-RPC bridge traffic
   without throughput issues. Fallback is per-platform WKWebView / Android WebView shells.
3. External folder access model. iOS security-scoped URLs and Android SAF tree URIs both need
   persisted permission handling; ProjectService currently assumes free-form absolute paths.
4. iOS AOT / trimming. The managed image/PDF libs (SixLabors.ImageSharp, SixLabors.Fonts,
   PdfSharpCore, Markdig, MessagePack) are cross-platform but must survive AOT + trimming on iOS;
   verify no reflection breakage.
5. App Store review. Fully offline, no arbitrary code download; extensions webviews are deferred,
   which also sidesteps the store's remote-code concerns for v1.

---

## 11. Repo rules the implementation must honor

From `CLAUDE.md`:

- No emojis anywhere (code, locales, UI, docs prose). Use text labels / lucide icons / SVG paths.
- 100% line coverage is gated for Novalist.Core / Sdk / Backend. New backend code (the in-process
  host, the mobile service stubs) ships with tests in the same change. The MAUI shell interop is the
  accepted native-exclusion category, kept behind a thin, clearly-named seam.
- Feature changes update the user manual (`docs/manual/`) and README. The mobile apps and any changed
  platform behavior get manual pages / README bullets when they ship.
- New dedicated renderer views need an activity-bar entry (unlikely for this port, but applies if
  any are added).
- Sizes and colors come from design tokens (`app/src/renderer/src/styles/tokens.css`); the responsive
  mobile layout must use `var(--nl-*)`, not hardcoded literals.
- The diagnostic log must never contain story content.
- When a scope or design decision is ambiguous, ask the product owner before implementing.
