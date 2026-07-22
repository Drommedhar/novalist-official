# iOS signing and TestFlight

How the MAUI iOS app (`Novalist.Mobile`, bundle id `com.novalist.mobile`) is built
and shipped to TestFlight. The `ios-app-store` job in
`.github/workflows/release.yml` does the work; this file covers the one-time setup
and the manual App Store Connect steps that cannot be automated.

Companion to `docs/macos-signing.md` — the iOS pipeline deliberately **reuses** two
of the macOS pieces, so most of the hard setup is already done.

## What is reused (already configured for the Mac App Store)

- **App Store Connect API key** — secrets `APPLE_API_KEY_BASE64`, `APPLE_API_KEY_ID`,
  `APPLE_API_ISSUER`. Account-level; `xcrun altool` uses it to upload the iOS build
  too.
- **Apple Distribution certificate** — secrets `MAS_CSC_LINK` (base64 `.p12`) and
  `MAS_CSC_KEY_PASSWORD`. "Apple Distribution" is the unified cert type: the same
  cert that signs the Mac App Store `.app` signs the iOS app. No separate iOS
  distribution certificate is needed.

## What is new (one-time, manual on Apple's side)

1. **Register the App ID.** developer.apple.com -> Certificates, IDs & Profiles ->
   Identifiers -> `+` -> App IDs -> App. Description `Novalist Mobile`, **Explicit**
   Bundle ID `com.novalist.mobile`, no capabilities (the app has no special
   entitlements). We had only used a wildcard *development* profile before.

2. **Create the App Store Connect app record.** App Store Connect -> Apps -> `+` ->
   New App -> iOS -> pick the `com.novalist.mobile` Bundle ID (it appears once the
   App ID above exists). This is a separate record from the macOS app (different
   bundle id).

3. **Create the iOS App Store provisioning profile.** Profiles -> `+` ->
   Distribution -> **App Store** -> App ID `com.novalist.mobile` -> select the **same
   Apple Distribution certificate** used for the Mac App Store build (so the CI cert
   secret matches) -> download the `.mobileprovision`.

## The new secrets

| Secret | Value |
| --- | --- |
| `IOS_APPSTORE_PROVISION_BASE64` | base64 of the iOS App Store `.mobileprovision` |
| `IOS_DIST_CSC_LINK` | base64 of the `.p12` for the Apple Distribution cert the profile is tied to |
| `IOS_DIST_CSC_KEY_PASSWORD` | that `.p12`'s export password |

`IOS_DIST_CSC_*` are only needed when the iOS App Store profile is tied to a
**different** Apple Distribution certificate than the Mac App Store build (e.g. you
created a fresh cert during iOS setup — you can end up with two "Apple Distribution"
certs of the same name but different serials, and the profile matches exactly one).
If the profile reuses the Mac App Store's cert, leave these unset and the job falls
back to `MAS_CSC_LINK`.

To build the `.p12` from the `.cer` you downloaded plus the private key from your
CSR (Windows, Git Bash / OpenSSL — same as the MAS cert in `docs/macos-signing.md`):

```sh
openssl x509 -inform DER -in ios_distribution.cer -out ios_distribution.pem
openssl pkcs12 -export -inkey ios_distribution_key.pem -in ios_distribution.pem -out ios_dist.p12
# then base64 ios_dist.p12 -> IOS_DIST_CSC_LINK, export password -> IOS_DIST_CSC_KEY_PASSWORD
```

Encode it the same way as the macOS profile (see `docs/macos-signing.md`):

```powershell
# Windows (PowerShell)
[Convert]::ToBase64String([IO.File]::ReadAllBytes("Novalist_iOS_AppStore.mobileprovision")) | Set-Clipboard
```

```sh
# macOS/Linux
base64 -i Novalist_iOS_AppStore.mobileprovision | pbcopy
```

## How it runs

- **Manual dispatch** (Actions -> Release Standalone App -> Run workflow) is a
  **dry-run**: it builds and signs the IPA and uploads it as a workflow artifact
  (`novalist-ios-ipa`) but does **not** upload to App Store Connect. Use this first
  to confirm the runner can build the app.
- **Pushing a tag** builds the IPA and uploads it to TestFlight via
  `xcrun altool --upload-app -t ios` with the API key. The build number is the CI
  run number (unique + increasing, which TestFlight requires); the version prefix
  comes from the tag.

The job selects the newest Xcode on the runner (net10.0-ios needs the iOS 26 SDK /
Xcode 26), restores the `maui-ios` workload, imports the Apple Distribution cert
into a temporary keychain, installs the provisioning profile, then
`dotnet publish -f net10.0-ios -c Release` produces the signed `.ipa`.

## Notes / gotchas

- **Runner Xcode.** If the `Select newest Xcode` step prints an Xcode older than 26,
  the build will fail on the iOS 26 SDK. Pin a specific image (e.g. `macos-26`) or
  add the SDK once GitHub's images catch up.
- **First TestFlight build** also needs App Store Connect **export-compliance** and
  **test information** filled in before the build is installable (same as macOS).
- The profile must be tied to the **same** Apple Distribution cert as `MAS_CSC_LINK`;
  if you used a different cert, add its `.p12` as a dedicated secret and point the
  `Import signing certificate` step at it instead of `MAS_CSC_LINK`.
