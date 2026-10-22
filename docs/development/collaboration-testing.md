# Test collaboration locally

All implementation and server changes are local. Publication waits for your testing
and explicit approval. Native Steam testing still requires Novalist's registered
app and two entitled Steam accounts; see [Steam builds](steam-collaboration.md).

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
Open a project copy in the owner instance; the guest downloads into a new folder.

## Direct TCP

In **Settings → Collaboration**, select **Direct connection (TCP)** in both apps.
The owner enters a display name, `127.0.0.1` and port `0`, then selects **Host direct
session** and **Create invitation**. The guest enters another display name, pastes
the invitation, chooses a new local folder and selects **Join direct session**.
TCP sessions require the host to remain open.

## Local hosted server

This workspace has a local server checkout, PostgreSQL and migration tool under
`app/out/collaboration-*`. These are development fixtures and are excluded from
desktop installers. If the computer has restarted, start the existing database
fixture again. From `app`:

```powershell
$pg = "$PWD/out/collaboration-postgres/pgsql/bin/pg_ctl.exe"
$data = "$PWD/out/collaboration-postgres-data"
$port = (Get-Content out/collaboration-postgres-port.txt).Trim()
& $pg status -D $data
if ($LASTEXITCODE -ne 0) {
    & $pg start -D $data -l "$PWD/out/collaboration-postgres.log" -o "-h 127.0.0.1 -p $port" -w
}
dotnet build out/collaboration-sync-server/src/Novalist.Sync.Server/Novalist.Sync.Server.csproj
```

In another terminal, from `app`, run:

```powershell
npm run collab:server
```

Copy the printed server URL into **Settings → Collaboration** in both apps. The
helper initially signs in **Morgan**, the owner. Sign in and share the project in
the owner app. Enter `guest` in the helper terminal, then sign in as **Alex** in the
guest app. Create an invitation in the owner app and accept it in the guest app.
Enter `third` before signing in from a third independent profile to test **Sam**.
The selected account applies to the next sign-in; existing sign-ins keep theirs.

Keep the helper terminal running while closing/reopening desktop clients or testing
owner-offline editing and first-time joining. Its sign-in provider is for local
testing only and asks for no credentials. Entering `quit` removes its temporary
server database. Your local project copies and app profiles remain; the old hosted
session cannot be resumed after that database is removed.

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
- In hosted mode, close the owner app and keep editing as the guest. Accept an
  unused invitation from the third profile while the owner remains closed.
- Reopen the owner. Remove a collaborator, check that remaining writers can
  continue, and invite the removed account again with a fresh invitation.
- End a session, open/export the retained local copies, and make an independent
  copy. Start a fresh session and check that old pending edits do not replay.
