# macOS signing, notarization, and Mac App Store

Novalist ships to macOS through **two independent channels**. They use different
certificates and different pipelines and are configured separately.

| | Developer ID (direct) | Mac App Store (MAS) |
| --- | --- | --- |
| Output | `.dmg` on GitHub Releases | `.pkg` uploaded to App Store Connect |
| Updates | built-in auto-updater | App Store |
| Certificates | Developer ID Application | Apple Distribution + Mac Installer Distribution |
| Sandbox | no | **yes (mandatory)** |
| Notarization | yes | no (App Review instead) |
| Workflow job | `publish` (matrix) | `mac-app-store` (matrix) |

Both are wired into [.github/workflows/release.yml](../.github/workflows/release.yml)
and driven by [app/electron-builder.yml](../app/electron-builder.yml). This page is
the operator runbook: which certificates to create, which GitHub secrets to set,
and the manual App Store Connect steps that cannot be automated.

---

## 1. Prerequisites (both channels)

You need, from <https://developer.apple.com/account>:

- Your **Team ID** (Membership page, 10 characters).
- An **App Store Connect API key** (Users and Access -> Integrations -> App Store
  Connect API -> generate a key with the **App Manager** role). Download the
  `.p8` **once** — it cannot be re-downloaded. Note the **Key ID** and **Issuer
  ID**. This single key is used for both notarization and App Store upload.

Encode any file (`.p12`, `.p8`, `.provisionprofile`) for a GitHub secret. The
secret must be a single unwrapped line — do NOT use `certutil -encode`, which
adds header lines and wraps at 64 chars.

```powershell
# Windows (PowerShell) — copies straight to the clipboard
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\path\to\file.p8")) | Set-Clipboard
```

```bash
base64 -i AuthKey_XXXX.p8 | pbcopy      # macOS
base64 -w0 AuthKey_XXXX.p8              # Linux
```

Store all secrets under **Settings -> Secrets and variables -> Actions** in the
GitHub repo.

### Creating the certificates on Windows (no Mac needed)

All three signing certificates (Developer ID Application, Apple Distribution,
Mac Installer Distribution) are made the same way. A certificate is your public
key signed by Apple; the private key is generated locally and never leaves your
machine. On Windows, use the OpenSSL bundled with Git for Windows (Git Bash).

**One key/CSR can be reused for all three certificates** — you only generate the
key once.

1. Generate the private key and a Certificate Signing Request. In Git Bash the
   `MSYS_NO_PATHCONV=1` prefix is REQUIRED, otherwise Git mangles the leading
   `/` in `-subj` into a Windows path:

   ```bash
   openssl genrsa -out novalist.key 2048
   MSYS_NO_PATHCONV=1 openssl req -new -key novalist.key -out novalist.csr \
     -subj "/emailAddress=you@example.com/CN=Novalist Signing/C=DE"
   ```

2. On <https://developer.apple.com/account> -> Certificates -> **+**, pick the
   certificate type (see sections 2 and 3), upload `novalist.csr`, and download
   the resulting `.cer`. Re-upload the SAME `novalist.csr` for each of the three
   types. Only the **Account Holder** can create Developer ID certificates.

3. Bundle the downloaded `.cer` with the private key into a password-protected
   `.p12`. Apple's `.cer` is DER-encoded, so convert it first:

   ```bash
   MSYS_NO_PATHCONV=1 openssl x509 -inform DER -in downloaded.cer -out cert.pem
   MSYS_NO_PATHCONV=1 openssl pkcs12 -export \
     -inkey novalist.key -in cert.pem \
     -name "Developer ID Application" \
     -passout pass:"YOUR_EXPORT_PASSWORD" \
     -out novalist.p12
   ```

   The export password becomes the matching `*_KEY_PASSWORD` secret.

4. Sanity checks — confirm the cert type and that it matches your key:

   ```bash
   # subject should name the expected cert type; issuer identifies the CA
   MSYS_NO_PATHCONV=1 openssl x509 -inform DER -in downloaded.cer -noout -subject -issuer
   # these two MD5s must be identical, proving the cert pairs with the key
   MSYS_NO_PATHCONV=1 openssl pkey -in novalist.key -pubout | openssl md5
   MSYS_NO_PATHCONV=1 openssl x509 -inform DER -in downloaded.cer -noout -pubkey | openssl md5
   ```

5. base64-encode the `.p12` (PowerShell command above) into its `*_CSC_LINK`
   secret.

> Keep the private key (`novalist.key`) and the `.p12` files backed up somewhere
> safe and OUT of git. The repo `.gitignore` already excludes `certs/`, `*.key`,
> `*.csr`, `*.cer`, `*.p12`, `*.p8`, and `*.provisionprofile`. Losing the key
> means re-issuing every certificate.

Shared secrets used by both channels:

| Secret | What it is |
| --- | --- |
| `APPLE_API_KEY_BASE64` | base64 of the App Store Connect API `.p8` |
| `APPLE_API_KEY_ID` | the API key's Key ID |
| `APPLE_API_ISSUER` | the API key's Issuer ID |

---

## 2. Developer ID (direct download `.dmg`)

This replaces the old ad-hoc `codesign --sign -` hack. The DMG is properly signed
and notarized, so it opens with a normal double-click and the auto-updater keeps
working.

### Certificate

Create a **Developer ID Application** certificate via the CSR flow in "Creating
the certificates on Windows" above (in the portal it lives under the
**Developer ID** section, near the bottom, visible only to the Account Holder).
Its subject reads `Developer ID Application: <you>` and its issuer is the
**Developer ID Certification Authority** — if the subject says "Apple
Development" or "Apple Distribution", it is the wrong type. Bundle it into
`novalist-devid.p12`.

On a Mac you could instead export it from Keychain Access, but the CSR flow works
identically without one.

### Secrets

| Secret | What it is |
| --- | --- |
| `MAC_CSC_LINK` | base64 of the Developer ID Application `.p12` |
| `MAC_CSC_KEY_PASSWORD` | the password you set on the `.p12` |

(Notarization reuses the shared `APPLE_API_*` secrets above.)

### How it runs

The `publish` job's "Package (electron-builder)" step sets `CSC_LINK` /
`CSC_KEY_PASSWORD` (only on the macOS runners) plus `APPLE_API_KEY*`.
electron-builder auto-discovers the Developer ID identity, signs with the
hardened runtime and [entitlements.mac.plist](../app/build/entitlements.mac.plist),
and notarizes + staples the DMG. Nothing else is required — tag a release and the
signed, notarized DMGs appear on the GitHub Release as before.

Local `npm run package` builds have no certificate, so electron-builder skips
signing and [afterPack.cjs](../app/build/afterPack.cjs) falls back to an ad-hoc
sign (it bails out automatically when `CSC_LINK` is set).

---

## 3. Mac App Store

> **Status: build + upload pipeline is in place, but the sandboxed app has NOT
> been validated on-device. Read "Sandbox feasibility" below before spending time
> on submission — Novalist's architecture needs verification (and likely code
> changes) before it will pass App Review.**

### One-time App Store Connect / portal setup (manual)

1. Register the App ID `com.novalist.app` (Certificates, Identifiers & Profiles
   -> Identifiers) as an **explicit** (not wildcard) App ID. Note: **App Sandbox
   is NOT an App ID capability** — that capabilities list is mostly iOS features,
   and the sandbox is declared by the app's entitlements plist instead. Leave all
   capabilities unchecked (Novalist uses none: no iCloud, App Groups, Push, etc.).
2. Create two certificates via the CSR flow above — reuse the same `novalist.csr`:
   - **Apple Distribution** (signs the `.app`). Subject: `Apple Distribution: <you>`.
   - **Mac Installer Distribution** (signs the `.pkg`). Subject in the portal is
     labelled "Mac Installer Distribution" but the cert's CN reads
     `3rd Party Mac Developer Installer: <you>` — that is the correct one.
3. Create a **Mac App Store** provisioning profile (Profiles -> + ->
   Distribution -> Mac App Store) for the App ID, tied to the **Apple
   Distribution** certificate. Download the `.provisionprofile`. Verify it embeds
   the right App ID / cert:

   ```bash
   PLIST=$(MSYS_NO_PATHCONV=1 openssl smime -inform DER -verify -noverify -in Novalist.provisionprofile 2>/dev/null)
   echo "$PLIST" | grep -A1 application-identifier   # -> <TEAMID>.com.novalist.app
   echo "$PLIST" | grep -A2 '<key>Platform</key>'    # -> OSX
   ```

4. Create the app record in App Store Connect (My Apps -> +). Notes:
   - The **store name must be globally unique**. "Novalist" was taken, so the
     listing name is **"Novalist - Novel Writing"**; the on-device app name stays
     "Novalist" (that is `productName`, unrelated to the store name).
   - **SKU** is a private internal identifier of your choosing (e.g. `novalist`);
     users never see it and it cannot be changed later.
   - The API upload can only deliver builds to an app record that already exists,
     but "1.0 Prepare for Submission" metadata (screenshots, description) is NOT
     needed to receive a build — only to submit it for review.

### Secrets

| Secret | What it is |
| --- | --- |
| `MAS_CSC_LINK` | base64 of the Apple Distribution `.p12` |
| `MAS_CSC_KEY_PASSWORD` | its password |
| `MAS_INSTALLER_CSC_LINK` | base64 of the Mac Installer Distribution `.p12` |
| `MAS_INSTALLER_CSC_KEY_PASSWORD` | its password |
| `MAS_PROVISION_PROFILE_BASE64` | base64 of the `.provisionprofile` |

### How it runs

The `mac-app-store` job builds the `mas` target (per arch), signs the app with
the Apple Distribution cert and the `.pkg` with the Mac Installer Distribution
cert, embeds the provisioning profile, applies the sandbox entitlements
([entitlements.mas.plist](../app/build/entitlements.mas.plist) +
[entitlements.mas.inherit.plist](../app/build/entitlements.mas.inherit.plist)),
then validates and uploads to App Store Connect with `xcrun altool`. The build
then appears under TestFlight / "Builds" for you to submit for review manually.

The job is independent of the GitHub Release job, so a MAS failure never blocks
the DMG/exe/AppImage release.

### Export compliance

`ITSAppUsesNonExemptEncryption: false` is baked into `Info.plist` via
`mac.extendInfo` in [electron-builder.yml](../app/electron-builder.yml) (inherited
by the `mas` block), so App Store Connect / TestFlight never prompt for export
compliance per build. `false` is correct while Novalist uses only exempt
encryption (standard HTTPS / OS-provided crypto). If it ever ships proprietary
encryption, change this to `true` and supply the compliance documentation.

### Sandbox — what's handled in code, and what still needs a device

The App Sandbox is mandatory on the Mac App Store. Several parts of Novalist's
design collide with it. The code-side mitigations are now in place, gated on
`process.mas` so they are complete no-ops on Windows, Linux, and the Developer ID
DMG. **None of these have been verified on a real Mac** — that requires a
TestFlight install (neither CI nor a Windows dev box can exercise the sandbox).

Implemented (still to be verified on-device):

1. **Spawned .NET backend extraction.** The self-contained single-file backend
   extracts native libs at startup; on darwin the launcher points
   `DOTNET_BUNDLE_EXTRACT_BASE_DIR` at `userData/backend-cache` (container-local)
   so the sandbox permits it ([backend-process.ts](../app/src/main/backend-process.ts)).
   The child also inherits the sandbox via
   [entitlements.mas.inherit.plist](../app/build/entitlements.mas.inherit.plist).

2. **Reopening a project from a stored path.** Within one session, a folder the
   user picks in the native panel is accessible to the app and its child backend.
   Reopening a recent project on a later launch has no fresh grant, so we capture
   a **security-scoped bookmark** at pick time and resolve it before
   `project/open`: [mac-bookmarks.ts](../app/src/main/mac-bookmarks.ts),
   `pick-folder` in [dialogs.ts](../app/src/main/dialogs.ts), and the
   `beginProjectAccess` gate in
   [projectStore.ts](../app/src/renderer/src/stores/projectStore.ts). If no valid
   bookmark exists, the app re-prompts for the folder rather than failing.

3. **Self-update disabled.** Apple forbids self-updating on the App Store, so the
   download-and-run-installer flow is off in MAS builds
   ([preload](../app/src/preload/index.ts) `autoUpdate`, guarded again in
   [index.ts](../app/src/main/index.ts)).

Open items requiring on-device work / a product decision:

- **Bookmark hand-off across a backend restart.** Access granted to the main
  process is shared with the child through the container, but confirm the backend
  still reads/writes the project after a `backend-restarted` supervisor restart.
- **Recent-project cover thumbnails.** `project/recent` reads each stored
  `CoverImagePath` on boot before any project is open; if a cover lives in a
  not-yet-accessible folder the thumbnail simply won't load (no crash). Acceptable
  to start; revisit if it looks bad.
- **Architecture / universal build.** The job builds arm64 and x64 as separate
  `.pkg`s with distinct build numbers. App Store Connect lets you upload multiple
  builds but you submit **one** per app version, so a per-arch build excludes the
  other architecture's users. The App Store norm is a single **universal** build —
  which for Novalist means a universal (lipo'd or dual) backend selected at
  runtime. Decide this before the first real submission.

Recommended path: install the build from TestFlight (internal testers, no review
needed), exercise open/reopen/import/export, fix whatever the sandbox surfaces,
then open App Review.

---

## Secret checklist

```
# Shared
APPLE_API_KEY_BASE64
APPLE_API_KEY_ID
APPLE_API_ISSUER

# Developer ID (.dmg)
MAC_CSC_LINK
MAC_CSC_KEY_PASSWORD

# Mac App Store (.pkg)
MAS_CSC_LINK
MAS_CSC_KEY_PASSWORD
MAS_INSTALLER_CSC_LINK
MAS_INSTALLER_CSC_KEY_PASSWORD
MAS_PROVISION_PROFILE_BASE64
```
