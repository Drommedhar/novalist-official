# Steam collaboration builds

The default build supports hosted servers and direct TCP without Steam. Set
`EnableSteamworks=true` to compile the optional Steamworks.NET adapter. The
adapter calls SteamNetworkingSockets P2P listen/connect APIs and carries the
existing pinned-TLS session protocol through reliable Steam messages. Invitations
contain the host's Steam identity and the same single-use project secret; a Steam
connection alone does not grant permission to read or change a project.

Compile the optional code with:

```powershell
dotnet build Novalist.Backend -p:EnableSteamworks=true
```

Publishing additionally requires `SteamworksNativeLibrary` pointing to the
platform's native library from your licensed Steamworks SDK, for example:

```powershell
dotnet publish Novalist.Backend -r win-x64 -p:EnableSteamworks=true -p:SteamworksNativeLibrary=C:\steamworks\sdk\redistributable_bin\win64\steam_api64.dll
```

Use the matching `libsteam_api.so` or `libsteam_api.dylib` for Linux or macOS.
The NuGet binding does not distribute these native libraries. Its current pinned
version targets Steamworks SDK 1.60. Include the resulting backend publish output
in the Electron package using the ordinary desktop packaging process.

Steam must be running and signed in to an account entitled to the registered
Novalist app. Steam launches provide `SteamAppId`; development launches may set
`NOVALIST_STEAM_APP_ID` to that same app ID. No demo app ID is selected implicitly.
The Settings form explains initialization failures and disables host/join when
Steam is unavailable.

Native integration remains unvalidated until two entitled Steam accounts can run
the same app build. Required acceptance: host a project, join from another account
without a reachable TCP endpoint, transfer an asset larger than one Steam message,
edit text simultaneously, observe cursors and field locking, reconnect after a
network interruption, and end while retaining both local copies. The TCP version
of this workflow is covered by `app/e2e/collaboration-peer.spec.ts`; compilation
does not substitute for this Steam acceptance.

Primary references: [Valve's networking API](https://partner.steamgames.com/doc/api/ISteamNetworkingSockets),
[Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET), and
[the pinned binding package](https://www.nuget.org/packages/Steamworks.NET/2024.8.0).
