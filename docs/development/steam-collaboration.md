# Steam collaboration builds

The default build supports hosted servers and direct TCP without Steam. Set
`EnableSteamworks=true` to compile the optional Steamworks.NET adapter. The
adapter calls SteamNetworkingSockets P2P listen/connect APIs and carries the
existing pinned-TLS session protocol through reliable Steam messages. Invitations
contain the host's Steam identity and the same single-use project secret; a Steam
connection alone does not grant permission to read or change a project.

## SDK and binding

Steam builds use SDK **1.65** and the compatible MIT-licensed Steamworks.NET
binding pinned to commit
[`ba71581f1ed7349e8d0f17ddc6f135dd3bc8a6a3`](https://github.com/rlabrecque/Steamworks.NET/commit/ba71581f1ed7349e8d0f17ddc6f135dd3bc8a6a3).
The NuGet package `2024.8.0` targets SDK 1.60 and is not used. Mixing that package
with newer native DLLs can fail during initialization with a missing
`SteamAPI_ISteamClient_GetISteamGameSearch` entry point.

Obtain the SDK yourself under [Valve's SDK access agreement](https://partner.steamgames.com/documentation/sdk_access_agreement/).
Do not commit the SDK ZIP, headers, tools or native binaries. The Windows helper
extracts only `sdk/redistributable_bin/win64/steam_api64.dll` from your local ZIP
into ignored `app/out/steamworks-native`. Section 1.1(b) of the agreement permits
distribution of files from `redistributable_bin` with the compiled application;
the complete SDK is not a release artifact.

The binding preparation downloads a fixed source archive, verifies its SHA-256,
and extracts only managed runtime sources and their MIT license into ignored
`app/out/steamworks-binding`. It does not extract SDK headers or tools. The small
`eng/SteamworksBinding` project compiles those sources and copies
`Steamworks.NET.LICENSE.txt` beside the backend, including on publish. The first
preparation needs GitHub access; subsequent builds reuse the verified archive.
Default non-Steam builds require neither the SDK nor this preparation.

Build on Windows with PowerShell 7:

```powershell
pwsh -File eng/Build-CollaborationSteam.ps1 -SteamworksSdkArchive C:\path\to\steamworks_sdk_165.zip
```

The helper also checks required native entry points before building, so an old
DLL fails with a setup error rather than an initialization error in the app.
For a manual Windows publish:

```powershell
pwsh -File eng/Prepare-SteamworksBinding.ps1
$nativePath = & ./eng/Get-SteamworksNativeLibrary.ps1 -SteamworksSdkArchive C:\path\to\steamworks_sdk_165.zip
dotnet publish Novalist.Backend -r win-x64 -p:EnableSteamworks=true "-p:SteamworksNativeLibrary=$nativePath"
```

On Linux or macOS, prepare the binding with PowerShell 7 and pass the matching
SDK 1.65 `libsteam_api.so` or `libsteam_api.dylib` to `SteamworksNativeLibrary`
when building on that platform. The archive-extraction helper is Windows-only.
Include the resulting backend publish output in the Electron package using the
ordinary desktop packaging process. Native runtime testing below currently
covers Windows x64.

Steam must be running and signed in to an account entitled to the registered
Novalist app. Steam launches provide `SteamAppId`; development launches may set
`NOVALIST_STEAM_APP_ID` to that same app ID. No demo app ID is selected implicitly.
The Settings form explains initialization failures and disables host/join when
Steam is unavailable.

## VS Code launch: App ID 480

On Windows, select **Collaboration: Steam (App ID 480)** in **Run and Debug** and
press **F5**. Its preparation task builds the optional adapter and copies the
native DLL into `app/out/collaboration-steam-build`; the launch selects that
backend explicitly and sets `NOVALIST_STEAM_APP_ID`, `SteamAppId` and `SteamGameId`
to `480`. The ordinary backend output is separate. Its settings and browser
profile persist under `app/out/manual-steam`.

The build uses `SteamworksNativeLibrary` from the environment if set. Otherwise,
it extracts the DLL from `SteamworksSdkArchive`, defaulting to
`%USERPROFILE%\Downloads\steamworks_sdk_165.zip`. Both values can also be passed
as parameters to `eng/Build-CollaborationSteam.ps1`. For a custom location, set
the environment variable before opening VS Code. A missing SDK or incompatible
DLL stops the preparation task with an actionable error. No DLL is borrowed from
another project.

Start Steam and sign in with an account that can run App ID 480. This ID is an
explicit local test setting, not Novalist's release identity. Testing a Steam
peer connection still requires a second Steam client/account; the two local
desktop profiles are intended for hosted/TCP tests.

## Runtime validation

For a macOS backend cross-published from Windows, pass
`-r osx-arm64 -p:SteamworksWindows=false` and point `SteamworksNativeLibrary`
at SDK 1.65's `redistributable_bin/osx/libsteam_api.dylib`. The explicit binding
platform is required because .NET removes the runtime identifier when building
referenced libraries. It selects `steam_api` and four-byte callback packing;
the Windows binding uses `steam_api64` and eight-byte packing. Package this backend
with the ARM64 Electron runtime and preserve Unix executable permissions and
framework symlinks. Perform the final bundle signing on the Mac.
Normalize the packaged entitlements plist to LF line endings: macOS 27's signing
parser rejects CRLF with `AMFIUnserializeXML: syntax error near line 1`.

The opt-in desktop regression test exercises the actual native runtime: Steam
initialization, reading the friends list, hosting a temporary project, ending and
hosting again, and handling invitation launch arguments while open and at startup.
It does not send invitations to real friends. Run it after the Steam backend and Electron app are built,
with Steam running and an account entitled to the selected app:

```powershell
cd app
$env:NOVALIST_STEAM_INTEGRATION = '1'
$env:NOVALIST_STEAM_APP_ID = '480'
npx playwright test e2e/collaboration-steam-runtime.spec.ts
```

The automated native regression has been validated on Windows. Live testing on
2026-10-23 with SDK 1.65 and Spacewar App ID 480 confirmed Windows-to-Apple-Silicon
invitation acceptance, initial project transfer, edits in both directions,
cursor names, and ending and rejoining using the existing local copy. Both peers
used SteamNetworkingSockets. The automated test is skipped in ordinary CI because
CI has no signed-in Steam client; it does not itself validate a second user's
connection. Remaining Steam acceptance includes an asset larger than one Steam
message, field-lock contention, and reconnecting after a network interruption.
The TCP version
of this workflow is covered by `app/e2e/collaboration-peer.spec.ts`; compilation
does not substitute for this Steam acceptance.

## Steam friend invitations

The host's **Invite Steam friends** control lists friends through `ISteamFriends`
and sends a fresh single-use invitation to the selected friend with
[`InviteUserToGame`](https://partner.steamgames.com/doc/api/ISteamFriends#InviteUserToGame).
The invitation uses a compact ASCII URL to fit Steam's 256-byte limit.
It carries the host Steam identity, session and project identities, pinned
certificate hash, secret and expiration. It is never published in rich presence.

For an already running app, `GameRichPresenceJoinRequested_t` queues the invite.
For a new launch, the Electron argument handler and `GetLaunchCommandLine` feed
the same inbox. Duplicate invitations are coalesced. Malformed, expired or
sender-mismatched callbacks cannot produce a join link. The native runtime stays
initialized between sessions; callbacks are pumped while the app is idle as well
as during a session, and the process shuts Steam down on exit.

The renderer displays an acceptance dialog and reuses the normal destination
picker. A new copy is the default; selecting an existing copy enables SHA-256
reuse and recovery of local differences. No join RPC is issued until the user
confirms both the invitation and destination. Existing sessions are never
automatically ended. The ordinary CI tests exercise the dialog's new/existing
copy paths using the local TCP transport; the native test covers the Steam SDK
and delivery into the dialog without messaging another account.

For a live test, keep both Steam clients online, start the rebuilt
app for both accounts, host on one, and invite the other. Verify the Steam invite
appears, accepting opens Novalist's folder dialog, and joining transfers the
project. Spacewar App ID 480 is only a local test identity: a Steam-triggered cold launch
uses that app's registered launch target. Testing Steam launching Novalist itself
requires Novalist's own registered app.

Primary references: [Valve's networking API](https://partner.steamgames.com/doc/api/ISteamNetworkingSockets),
[Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET), and
[Valve's API integration and native library requirements](https://partner.steamgames.com/doc/sdk/api).
