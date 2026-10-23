# Test collaboration locally

The desktop collaboration branch and the community-server fork are published for review.
Native Steam initialization and hosting can be tested with the App ID 480 launch;
a full peer test requires two entitled Steam accounts. See [Steam builds](steam-collaboration.md).

## HTTPS and secure WebSockets

After building the desktop and local community server, run from PowerShell 7 on Windows:

```powershell
./eng/Test-CollaborationHttps.ps1
```

This starts the real community server on HTTPS, an HTTPS test sign-in provider,
and two isolated Novalist instances. It exercises sign-in, invitation creation,
joining into a new local copy, editing in both directions over WSS and displayed
writer names. A separate case rejects an untrusted certificate. Certificate
validation remains enabled in the desktop, relay and sign-in clients.

The runner creates a short-lived localhost certificate, trusts that exact certificate
in the current Windows user's store, and removes it and the temporary private keys
in its cleanup. Node receives the same certificate through `NODE_EXTRA_CA_CERTS`.
This validates local HTTPS/WSS; a public deployment still needs its own trusted
certificate, public sign-in provider and WebSocket-capable HTTPS endpoint.

## VS Code launch configurations (Windows)

For a server without accounts, choose **Collaboration: Local server (no account) + 2 apps**
and press **F5**. Use `http://127.0.0.1:42670` under **Collaborate** in both apps,
enter **Your name**, then share or join directly. This launch starts no sign-in
provider. **Collaboration: Local server (no account)** starts just the relay.
Both server configurations use the same relay port, so stop the running server
before switching between them.

In **Run and Debug**, choose **Collaboration: Local server + 2 apps** and press
**F5**. It builds the desktop app and local relay server and opens owner and guest instances with separate persistent
settings and browser profiles. Use `http://127.0.0.1:42670` in both apps'
**Collaborate** in the top bar. The sign-in helper uses port `42671`; both addresses
remain stable across restarts. Do not enter `42671` as the server address: it only
handles browser sign-in and cannot relay a session. The terminal labels both
addresses when ready. If those ports
are already in use, stop the earlier helper or set `NOVALIST_COLLAB_RELAY_PORT` and
`NOVALIST_COLLAB_IDENTITY_PORT` to distinct available ports before launching.

Sign in as Morgan in the owner app, enter `guest` in the server terminal, then
sign in as Alex in the guest app. Create and accept an invitation as described
below. The **Collaboration: Owner**, **Collaboration: Guest** and
**Collaboration: Local server** entries can restart individual components; the
two app entries reuse the desktop build prepared by the compound launch.

Stopping one instance leaves the other app and server running. Closing the host
ends its session; a running relay cannot serve the project without it. Close the
clients when finished, then enter `quit` in the server terminal. Local project
copies and app profiles remain. Keep both the host and server running when testing
guest reconnects. The relay requires no database and stores no project data.

**Collaboration: Steam (App ID 480)** builds and launches the Steam variant with
its own profile. See [the Steam launch setup](steam-collaboration.md#vs-code-launch-app-id-480)
for the native library and account requirements. These development configurations
disable self-updates and require the existing local fixtures under `app/out`.

## Start two independent app instances

Build once from the repository root:

```powershell
dotnet build Novalist.Backend/Novalist.Backend.csproj
cd app
npm run build
```

In an owner PowerShell terminal, from `app`:

```powershell
$env:NOVALIST_SETTINGS_DIR = "$PWD/out/manual-owner/settings"
node node_modules/electron/cli.js out/main/index.js "--user-data-dir=$PWD/out/manual-owner/profile"
```

In a second terminal, also from `app`:

```powershell
$env:NOVALIST_SETTINGS_DIR = "$PWD/out/manual-guest/settings"
node node_modules/electron/cli.js out/main/index.js "--user-data-dir=$PWD/out/manual-guest/profile"
```

The separate settings and browser profiles give each instance its own account,
device and workspace. Reuse those commands to test closing and reopening the app.
Open a project copy in the owner instance. The guest can choose a new folder or
select **Use an existing local project** to reuse matching files.

## Direct TCP

In **Collaborate** in the top bar, select **Direct connection (TCP)** in both apps.
The owner enters a display name, `127.0.0.1` and port `0`, then selects **Host direct
session** and **Create invitation**. The guest enters another display name, pastes
the invitation, chooses a new folder or an existing copy and selects **Join direct session**.
TCP sessions require the host to remain open.

## Local relay server

This workspace has the locally modified community-server checkout under
`app/out/collaboration-sync-server`. It is excluded from desktop installers and
has not been published. Its relay mode needs no database or migrations. From `app`:

```powershell
dotnet build out/collaboration-sync-server/src/Novalist.Sync.Server/Novalist.Sync.Server.csproj
```

In another terminal, from `app`, run:

```powershell
npm run collab:server
```

To run without accounts instead, use `npm run collab:server -- --anonymous`.
Enter **Your name** in each app and share or join directly; no browser login is needed.

Copy the printed server URL into **Collaborate** in the top bar in both apps. The
helper initially signs in **Morgan**, the owner. Sign in and share the project in
the owner app. Enter `guest` in the helper terminal, then sign in as **Alex** in the
guest app. Create an invitation in the owner app and accept it in the guest app.
Enter `third` before signing in from a third independent profile to test **Sam**.
The selected account applies to the next sign-in; existing sign-ins keep theirs.

Keep the helper terminal and the host's session running. Its sign-in provider is
for local testing only and asks for no credentials. Entering `quit` stops packet
forwarding. Your local project copies and app profiles remain. Restarting the
relay or closing the host requires starting a new session. The helper's test
accounts and tokens last only for that helper run. After restarting it, sign in
again in both apps; an old rejected sign-in returns to the sign-in form. If an
already running app still shows an account from the previous run, sign out first.
Do not use the sign-in provider's port as the collaboration server address.

## Things to exercise

- Type and format in the same scene from both apps. Check cursors and each writer's
  undo. Open different scenes and inspect presence badges in the binder/outliner.
- Edit a scene in Manuscript while the other app uses the normal scene editor.
  Check formatting, each writer's undo and saved changes after reopening. Include
  a scene imported from Markdown with bold and italic text.
- Focus the same Codex field in both apps. Check that the second names its holder
  and becomes editable after the first leaves it. Try table cells, relationships,
  visibility, media labels, scene metadata and research controls.
- Move a scene card between POV or custom-property columns in Board view. Hold the
  corresponding field open in the other app and check that a blocked move names
  that writer; leave the field and try the move again.
- Add scenes, Codex entries, research and assets; rename, move and delete them.
  Navigate different books/drafts and check both retained copies.
- Interrupt the connection while editing shared prose, then reconnect. Delete a
  scene while the other writer has pending work and inspect **Recovered edits**.
- Join using an existing copy. Check reused-file counts, a changed file, a missing
  file and an asset larger than 1 MB. Verify local differences in **Recovered edits**.
- Cancel synchronization and verify the previous local project remains usable.
- Close the host and check that new joins fail clearly, while local copies remain.
  Reopen the host and start a fresh session with a new invitation.
- Remove a collaborator, check that remaining writers can continue, and invite
  the removed account again with a fresh invitation.
- End a session, open/export the retained local copies, and make an independent
  copy. Start a fresh session and check that old pending edits do not replay.
