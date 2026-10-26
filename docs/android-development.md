# Android development

Android work is on `feat/android-mobile`, based on `176673f3`. The shared MAUI host now builds an Android 13+ APK with the React editor and in-process backend. This is a development build: the agreed beta also requires linked external folders, Google Drive, OneDrive and Nextcloud, conflict recovery, and the remaining acceptance checks. Those storage integrations are not available yet.

## Windows toolchain

The build was verified on Windows with .NET SDK **10.0.303**, the **maui-android** workload (Android manifest **36.1.2**), MAUI Controls **10.0.110**, Microsoft OpenJDK **17.0.14**, SDK platform **36.1**, build tools **36.0.0**, command-line tools **22.0**, and emulator **37.2.12**. The renderer requires Node.js 22.13+ or 23.2+. WSL is unnecessary for the emulator.

Enable Windows Hypervisor Platform in an Administrator PowerShell if it is not enabled:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform -All -NoRestart
```

Restart Windows when required. The emulator's `-accel-check` must report usable WHPX acceleration. No build script restarts the computer.

Install the workload and dependencies from the repository root:

```powershell
dotnet workload install maui-android --skip-manifest-update
dotnet build Novalist.Mobile/Novalist.Mobile.csproj -f net10.0-android `
  -t:InstallAndroidDependencies -p:AcceptAndroidSdkLicenses=True `
  "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" `
  "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk"
Push-Location app
npm ci
Pop-Location
./eng/Build-Android.ps1
```

The script accepts `-AndroidSdkDirectory` and `-JavaSdkDirectory`, or `ANDROID_HOME` and `JAVA_HOME`. It builds the renderer, checks native build success, and prints the APK's SHA-256. Use `-Runtime android-arm64` for ARM64 hardware, and `-Configuration Release` to publish an APK into the runtime's `publish` directory. Local builds use development signing unless `-SigningKeyStore` is supplied with `ANDROID_SIGNING_PASSWORD`; the alpha workflow always requires its persistent signing key. `-SkipRenderer` is only for native changes after the web assets have already been built.

The build passes `EnableImportApi=false` and `UseMonoRuntime=true` globally so referenced desktop projects do not restore ASP.NET or CoreCLR Android runtime packs. Assemblies are embedded in Debug APKs; installing an APK that relies on Fast Deployment without the debugger causes a startup abort. See Microsoft's [Android build properties](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties).

On macOS the default target remains iOS, so existing iOS-only workload installations continue to work. Pass `-f net10.0-android` when building Android there. Windows defaults to Android and does not restore Apple workloads.

## Phone and tablet emulators

### Launch from VS Code

Open **Run and Debug** (`Ctrl+Shift+D`), select **Android: Phone (Android 16)** or **Android: Tablet (Android 17)**, and press **F5**. Each configuration builds the current renderer and Android APK, opens the corresponding visible emulator, waits for Android to boot, installs the app, and launches Novalist. The first launch can take a few minutes. An already running emulator is reused, and project data survives APK updates.

These are preview launchers using VS Code's built-in terminal launcher; they do not attach a C# debugger. Close the emulator window when finished. The launch script can also be run directly after building: `pwsh -File eng/Run-Android.ps1 -Device Phone` (or `Tablet`).

### Emulator setup

Set `JAVA_HOME` to the JDK directory. Install `emulator` and `system-images;android-36;google_apis_playstore;x86_64` with `sdkmanager.bat`. Create AVDs with `avdmanager.bat`, using the `pixel_7` and `pixel_tablet` profiles. The initial local names are `Novalist_Phone_API36` and `Novalist_Tablet_API36`.

```powershell
$androidSdk = "$env:LOCALAPPDATA\Android\Sdk"
& "$androidSdk\emulator\emulator.exe" -accel-check
& "$androidSdk\emulator\emulator.exe" -avd Novalist_Phone_API36
& "$androidSdk\platform-tools\adb.exe" devices
& "$androidSdk\platform-tools\adb.exe" -s emulator-5554 install -r `
  Novalist.Mobile/bin/Debug/net10.0-android/android-x64/com.novalist.app-Signed.apk
& "$androidSdk\platform-tools\adb.exe" -s emulator-5554 shell monkey `
  -p com.novalist.app -c android.intent.category.LAUNCHER 1
```

Use the serial printed by `adb devices`; a second AVD commonly uses `emulator-5556`. The Debug app exposes WebView debugging for inspecting the real renderer. Release validation must also run the installed Release APK, without relying on this debug channel.

Android 17 uses package `system-images;android-37.0;google_apis;x86_64`. Create its AVD with command-line tools 22.0 and check that the AVD's `.ini` contains `target=android-37.0`. Version 19 wrote `target=android-0` here, causing SurfaceFlinger to restart repeatedly before the app could install. Recreating the AVD with 22.0 resolved it. This matches the [reported tool-version issue](https://github.com/actions/runner-images/issues/14484). The corrected local AVD is `Novalist_Tablet_API37_Validated`.

On this Windows host, the preview launcher uses `-gpu host` for the API 36 phone and `-gpu swiftshader` for the API 37 tablet, with `-no-snapshot -no-metrics -feature -Vulkan` for both. SwiftShader caused repeated Windows emulator access violations on the API 36 phone; the host AMD graphics driver passed the phone UI and Back checks. Add `-no-window -no-audio` for headless checks. The older `swiftshader_indirect` mode caused emulator access violations on API 33. Assign distinct free console/ADB ports when switching AVDs and wait for the package service as well as `sys.boot_completed` before installing.

## Current behavior and acceptance evidence

- A project can be created in the app's private project directory, edited, saved and reopened. API 33 AOSP and API 36 Play phone tests typed German characters into the actual editor, saved, force-stopped the app and reopened the same persisted scene.
- Android uses the same screen layouts as iPhone and iPad: five icon-and-label phone tabs, an anchored Plan menu, and a grouped tablet sidebar that collapses to an icon rail. The expanded sidebar overlays the workspace and closes after choosing a destination or tapping outside. The host publishes the rail and tab-bar insets to the shared renderer, and window width selects the phone or tablet layout.
- Android Back uses the existing save-aware editor/project actions, returns from phone detail pages to their lists, and dismisses ordinary dialogs through their existing cancel actions. Busy dialogs retain their cancellation guards. Tablet sidebar drags start on the rail controls; the outer screen edge remains Android's system Back gesture.
- Android document/photo selection stages files in app cache; exports hand a cached file to the Android share sheet. Android reports the handoff, without claiming the recipient saved the export. Isolated `.scrivx`/`.scriv` manuscript selections are rejected because they require neighboring project files. Full Scrivener folder staging remains unfinished; export DOCX or RTF from Scrivener for now.
- Offline speech uses Android's on-device recognizer and native PCM capture. Availability depends on the installed recognition service and languages. Successful live EN/DE transcription and supplied-audio support must be recorded before calling this feature verified. No online fallback is configured.
- Read-aloud uses Android's native text-to-speech service, selecting only voices that do not require a network connection. The API 37 image listed nine offline voices; an English sentence completed, and Stop interrupted a longer passage. API 33 AOSP offered no voices. See the Android [text-to-speech contract](https://developer.android.com/reference/android/speech/tts/TextToSpeech).
- The debug-only SAF probe on API 33 created, updated, renamed, moved, read and deleted Unicode files. Folder access survived a restart; a separate restart confirmed revocation. This establishes picker/provider behavior, not a completed external-project editing feature.

The frozen parity baseline is repository commit `176673f3`, including mobile fixes after local tag `ios-v3.2.2` (`605efeea`). Phone navigation preserves Dashboard, Write, Codex, Plan and Settings. Tablet navigation preserves Dashboard, Write, Manuscript, Timeline, Plot Grid, Calendar, Relationships, Codex, Wiki, Maps, Research, Gallery, Export and Settings. Desktop extension execution and Git remain unavailable in the mobile host.

The Android UI parity pass checks all 14 tablet destinations in the actual mobile renderer with both Apple and Android host capabilities. Settings keeps its category column beside the form; populated property controls fit narrow tablet widths. The API 37 tablet APK passes native selection, sidebar overlay geometry, outside-tap and Back dismissal, horizontal dragging without accidental navigation, and vertical scrolling to Settings. The API 36 phone checks cover native tabs, Plan selection/dismissal, and Settings detail Back navigation. Automated checks pass **225 transport tests**, **20 Electron mobile UI tests**, and **6 actual mobile-bundle E2E tests**. TypeScript and all eight source checks pass; the changed-source Aislop scan reports **100/100 with no findings**. The Windows build has no compiler errors or new compiler warnings; existing NuGet vulnerability warnings remain.

Verification after the review fixes: **3,971 Core**, **2,092 Backend**, **61 Sdk**, **142 transport**, **4 mobile export E2E**, and **2 Electron mobile safe-area E2E** tests pass; Core/Backend/Sdk each retain **100% line coverage**. Desktop and mobile renderer builds, TypeScript, manual links and locale consistency pass. Aislop reports no errors or actionable warnings; its two remaining warnings mistake `AndroidX.Activity.Result` namespace imports for blocking `Task.Result` calls. Existing NuGet ImageSharp/MessagePack vulnerability warnings remain visible in build logs.

API 33 AOSP phone, API 36 Play phone/tablet, and API 37 Google APIs tablet have launched the real app. The API 37 initial bookshelf uses tablet layout before opening a project. Release x86_64 startup and opening an existing project were checked on API 36; ARM64 development packaging builds, but ARM64 runtime behavior was not tested. Full interrupted-save/WebView-process-loss recovery, complete media/import/export acceptance, successful offline dictation, all language/accessibility configurations, and iOS compilation remain outstanding. Windows cannot compile the iOS target locally; the existing Apple CI workflow remains the native compile gate. An x86_64 emulator pass does not establish ARM64 device behavior.

Review follow-ups retained for the beta: local-project picker messages need localization, and shared cache copies need an age-based cleanup policy that preserves recipient access. These do not change the requirement to finish the provider, recovery and signing gates before distributing a beta.

The API 37 tablet retained its open project when resized to a phone-width window and back. A microphone permission decision made after the 25-second native startup deadline did not start capture; a subsequent start/read/stop sequence succeeded. Both EN/DE recognition languages were supported but uninstalled on this image, so successful dictation is still unverified. Release installation retained the existing project. Release APK signature and ZIP 16 KiB alignment checks passed; the 496 packaged ARM64 native libraries have load-segment alignment of at least 16 KiB. The emulator ran with 4 KiB pages: these file checks do not establish 16 KiB runtime compatibility.

## Cloud setup required for the beta

The owner confirmed there are no Google/Microsoft app registrations or Nextcloud test server yet. Set up disposable accounts/projects before performing the provider contract tests:

- **Google:** a developer-owned Cloud project, Drive API, OAuth consent configuration and Android OAuth identity for `com.novalist.app` with the signing certificate fingerprint. Existing project trees require the accepted broader Drive scope; wider distribution requires the appropriate Google verification. Record development and eventual beta signing identities separately.
- **Microsoft:** a public client registration supporting the intended personal and work/school accounts, Android redirect identity for this package/signing certificate, delegated access to the user's own files, and tenant consent where required. Never embed a client secret in the app.
- **Nextcloud:** an HTTPS server URL and disposable account. Use Login Flow v2 for an app password, stored only in Android secure storage. Do not put passwords, tokens or manuscript content in source files or logs.

The selected scope is My Drive, the signed-in user's own OneDrive, and authenticated Nextcloud folders. Linked local working copies must remain editable offline after download. Uploads must reject stale writes or preserve both variants using a proven provider-specific strategy. A pre-upload version check alone does not protect against a racing desktop edit. No automatic cloud upload should ship before these contract tests pass.

## GitHub alpha releases

Normal version tags (for example `v3.6.2`) run the Android alpha build alongside the desktop release. The release waits for both Android architectures and attaches these files to the GitHub release:

- `novalist-android-alpha-arm64.apk` for ARM64 phones and tablets.
- `novalist-android-alpha-x64.apk` for x64 emulators.
- A `.apk.sha256` checksum beside each APK.

The APKs use Release configuration and retain `com.novalist.app`. Their display version is `<tag version>-alpha.<release workflow run number>`; the same run number is the Android version code. Keep that sequence increasing for future updates. Rerunning a workflow retains its version code. The APK signature is verified against the configured certificate before staging an artifact; missing signing secrets fail the build. Both architectures must succeed before the GitHub release is created. Android remains alpha even when the desktop tag is stable, so its asset names and release notes identify that status without changing the desktop release channel.

`ios-*` tags retain their existing iOS-only behavior. Manual release workflow runs build artifacts without publishing a GitHub release, and `ios_only` skips Android. The **Android APK** workflow can also be dispatched with `alpha: true` to check signed packaging independently. Its default and pull-request runs produce development APKs without accessing signing secrets.

The alpha signing identity uses alias `novalist-alpha` and these repository Actions secrets:

- `ANDROID_ALPHA_KEYSTORE_BASE64`: the PKCS#12 keystore encoded as Base64.
- `ANDROID_ALPHA_KEYSTORE_PASSWORD`: the shared keystore/key password.

Keep an independent backup of the keystore and password. On the maintainer's Windows workstation the backup is under `%USERPROFILE%\.android\novalist-signing\alpha`, outside Git and restricted to the owner and SYSTEM. Passwords are passed to the signing tools through environment references, following Microsoft's [Android signing guidance](https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli?view=net-maui-10.0). The runner's decoded keystore is removed after packaging. Do not replace this key between alpha releases; Android requires a compatible signing identity for updates. The development emulator APK uses a different certificate, so it cannot be updated in place with the alpha APK.

Local equivalent, with `ANDROID_SIGNING_PASSWORD` already set securely:

```powershell
./eng/Publish-AndroidAlpha.ps1 -Tag v3.6.2 -VersionCode 123 -Runtime android-arm64 `
  -SigningKeyStore "$env:USERPROFILE\.android\novalist-signing\alpha\novalist-alpha.keystore"
```

Before promoting the alpha to beta, finish external/cloud project storage and the remaining acceptance checks above, and verify an APK update preserves projects, grants and credentials. The desktop changelog intentionally excludes mobile-only work.

## Google Play later

The Android workflows build APKs only and contain no Google Play upload. Later Play distribution requires an AAB pipeline, compatible signing identity, then-current target API compliance, listing/privacy disclosures and staged rollout. No provider account creation is performed by the build scripts.
