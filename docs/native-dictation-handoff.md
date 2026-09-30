# Apple dictation handoff

Continue on a Mac in `novalist-official`, branch **dev**. The optional extension is in sibling `novalist-aiassistant`, branch **main**. No SSH connection is needed: open this repository in a local coding session and use this file as the task.

## Task and agreed behavior

Finish Apple compilation, packaging, and real microphone validation; fix any failures you find. Do not claim the Apple implementation works merely because Windows tests pass. Commit fixes to Novalist `dev`; extension fixes belong on AI Assistant `main`.

- Dictation inserts at the caret, preserves selected text, and needs no preview or acceptance step. Writers edit results normally.
- English and German are required. Long recordings are split into bounded clips and inserted in order. Stop must include the final phrase.
- Built-in native speech needs no extension and no Novalist-managed models. Apple-managed language downloads are explicitly accepted.
- Apple uses SpeechAnalyzer on macOS 26+ and iOS/iPadOS 27+. Prefer SpeechTranscriber; use DictationTranscriber when the former is unavailable for the device/language.
- Native speech inserts the recognized transcript. Automatic character dialogue inference, quotation style, and new dialogue paragraphs remain in AI Assistant's optional local Whisper/Qwen provider. Keep CUDA, ROCm, MLX, CPU, and Whisper Large v3 support there.
- Windows uses the OS Windows+H panel and Microsoft's online speech service, which the user explicitly accepted. Windows owns language selection and stopping in that mode.
- Native settings belong under **Settings → Writing assistance → System dictation**. Extension model settings remain under **Extensions → AI Assistant → Dictation**.

## State at handoff

At the original handoff, implementation had been exercised on Windows only. See **Apple validation — 2026-09-30** below for subsequent Mac results and the remaining iOS/live-device checks. The `Apple speech build` workflow compiles native Mac libraries and an iOS simulator app on a compatible runner; it cannot replace microphone/device testing.

Local validation includes 1,982 backend tests, TypeScript checking, desktop and mobile renderer builds, all eight source doctors, and 24 Playwright dictation tests (editor, audio/formatting, settings). Those tests use generated audio or mocked OS calls; they never turn on the user's microphone. A Windows test verifies caret restoration and insertion without opening the real Windows voice panel. Native settings preparation is tested without loading an extension. Real Windows microphone recognition also still requires a manual check.

The earlier AI Assistant implementation is commit `7816121`; its CPU and AMD ROCm English/German paths were exercised on Windows. Its Apple MLX and CUDA hardware paths still need suitable hardware. The initial host dictation/caret work is `f611748b`; the native implementation is the subsequent commit containing this document.

## Prerequisites

Use full **Xcode 27.0**, **.NET SDK 10.0.401**, workload set **10.0.401.1**, and **MAUI 10.0.110**. Xcode 27 requires macOS 26.6 or newer. Also install the .NET 8 runtime/SDK for the desktop backend and Node 22.12 or newer. These versions follow the [.NET Apple SDK release instructions](https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode27.0-10722).

Check the working tree before switching branches or pulling. Preserve local changes. Use a native ARM64 terminal/.NET installation on Apple Silicon. Install Xcode and any OS update through the user's normal setup; do not silently upgrade the machine.

From the repository root, after the toolchain is installed:

```sh
git switch dev
git pull --ff-only origin dev
xcodebuild -version
xcrun --sdk iphoneos --show-sdk-version
dotnet --version
dotnet workload install maui-ios --version 10.0.401.1
cd app
npm ci
npm run build
npm run build:mobile
cd ..
```

## Build and inspect

Compile the Swift layer first to isolate API/signature issues from MAUI:

```sh
bash native/AppleSpeech/build.sh macos arm64 artifacts/apple-speech/arm64/libNovalistSpeech.dylib
bash native/AppleSpeech/build.sh macos x86_64 artifacts/apple-speech/x64/libNovalistSpeech.dylib
bash native/AppleSpeech/build.sh simulator arm64 artifacts/apple-speech/simulator/libNovalistSpeech.a
bash native/AppleSpeech/build.sh ios arm64 artifacts/apple-speech/ios/libNovalistSpeech.a
dotnet build Novalist.Backend/Novalist.Backend.csproj
dotnet build Novalist.Mobile/Novalist.Mobile.csproj -f net10.0-ios27.0 -p:RuntimeIdentifier=iossimulator-arm64 -p:EnableCodeSigning=false
```

On an Intel Mac use `x86_64` for the simulator archive and `iossimulator-x64` for its runtime identifier. Use the existing iOS signing/provisioning setup for a physical-device build; the unsigned simulator command does not validate device AOT linkage.

Build the desktop app with `npm run package` from `app/`, then verify `libNovalistSpeech.dylib` is present beside the backend in the app's resources and is signed. Check `nm`, `otool -L`, and `codesign --verify --deep --strict` on the resulting native files/app as appropriate. A missing library must report unavailable rather than preventing startup on older Macs.

Relevant code:

- `native/AppleSpeech/NovalistSpeech.swift`: C ABI, async SpeechAnalyzer lifecycle, locale support and system asset installation. Consult [Apple's SpeechAnalyzer documentation](https://developer.apple.com/documentation/speech/speechanalyzer) for exact Xcode declarations.
- `native/AppleSpeech/AppleMicrophone.swift`: iOS AVAudioEngine capture, mono PCM16 WAV chunks, silence splitting, background/interruption cleanup. MAUI serves the renderer through `app://`, so iOS capture is native rather than browser getUserMedia.
- `native/AppleSpeech/AppleSpeech.Mac.targets` and `AppleSpeech.iOS.targets`: architecture selection, output paths, desktop dylib copying, static native reference and Swift system libraries. Verify both build and publish.
- `Novalist.Backend/Dictation/AppleSpeechBridge.cs`: callback marshalling, request lifetime/cancellation, macOS P/Invoke and iOS `__Internal` linkage. Device AOT must retain `nl_speech_request` / `nl_speech_cancel` and the unmanaged callback.
- `Novalist.Mobile/Services/SystemMicrophone.cs`, `Pages/RendererHostPage.cs`, and `app/src/renderer/src/mobile/shim.ts`: microphone permission and native capture bridge.
- `app/src/renderer/src/dictation/`: provider readiness, settings, capture queue and direct editor insertion. Native mode skips dialogue formatting.

## Real-device checks

Use short synthetic prose, never log a user's manuscript or recording.

1. Start with AI Assistant absent/disabled. Find System dictation settings. Confirm English and German readiness matches Apple's supported/installed locales. Prepare missing assets, exercise cancellation/retry, and confirm already-installed assets need no download.
2. Dictate both languages into the middle of a populated scene, including after a selected passage. Text must appear there once, with no preview, duplicated words, invented dialogue quotes, or movement to the end. Undo and autosave must work.
3. Speak through several pauses and for several minutes. Stop mid-phrase and verify the final clip is inserted. Move the caret while a clip is processing; queued speech stays at its original insertion point. Leaving a scene pauses pending speech for that scene.
4. Deny/regrant microphone permission; disconnect an external microphone. On iPhone/iPad also background the app, lock the device, and interrupt its audio session. Capture must release the microphone and keep bounded pending data. Test with a physical device; simulator speech assets/audio availability may differ.
5. After preparing both languages, disconnect networking and repeat Apple dictation. Test Apple Silicon first; check DictationTranscriber fallback on older supported hardware if available. Record the device/OS and selected transcriber; do not equate compilation with recognition accuracy.
6. Test the packaged Mac build and a signed iOS/iPadOS device build, including launch after a fresh install and permissions. Check App Store entitlements and Swift/AOT linking if the signed build differs from development.
7. Optionally validate the existing AI Assistant MLX backend on native Apple Silicon. It is separate from OS speech and requires its own model setup; do not introduce Ollama/LM Studio transcription or external transcription services.

Report concrete build results, hardware/OS tested, English/German outcomes, remaining limitations, and final commits. Update this file with Apple validation evidence when finished. Keep the manual and desktop changelog consistent with any user-visible fixes.

## Apple validation — 2026-09-30

The user requested **automated checks only; live microphone testing later**. No microphone was activated. These results do not constitute full device acceptance.

### Environment and toolchain blocker

- MacBook Pro `Mac17,8`, Apple M5 Pro, 48 GB, native ARM64; macOS 27.0.1 (`26A434`).
- Installed: Xcode 26.6 (`17F113`) with Apple SDKs 26.5; .NET 10.0.300, workload set 10.0.302, MAUI workload manifest 10.0.20; .NET 8.0.419 / runtime 8.0.25; Node 26.0.0.
- Both simulator and device app build commands for `net10.0-ios27.0` stop at **NETSDK1140**: the installed workload accepts only iOS 26.0/26.5. Install the prerequisites above before retrying; no machine toolchain or project target was downgraded/upgraded to conceal this failure.
- A paired iPhone 17 Pro Max (`iPhone18,2`) runs iOS 27.0 (`24A437`) with Developer Mode enabled. No app was installed on it during this run.

### Fixes and automated results

- Fixed native MSBuild output paths: resolve configuration/framework/runtime paths after SDK evaluation, and register the iOS native reference at build time. Previous evaluation produced `apple-speech/...` under the project and could reuse outputs across configurations. Preserve any existing untracked archive there; it is not validation evidence.
- Fixed electron-builder 26.15.3 signing: retain the certificate hash already selected by its helper. Passing the display name failed with two valid Apple Development certificates sharing that name. The existing version-pinned postinstall patch now covers both this and the earlier keychain-password workaround.
- Corrected the Apple editor test's line-start shortcut on macOS (`Home` does not position the caret as it does on Windows); explicitly assert the starting caret offset.
- Swift builds passed for macOS ARM64/x86_64 and iOS ARM64 device/simulator archives. The latter used SDK 26.5 with an iOS 27 deployment target and **do not validate Xcode 27, MAUI linkage, AOT, or a running iOS app**. The compiler reported the existing `AVAudioSession.recordPermission` deprecation on iOS.
- Backend Debug build and self-contained single-file Release publishes for Mac ARM64/x86_64 passed. A separate `--no-build` Intel publish also contains the correct x86_64 speech dylib. `nm` confirms both C entry points; `otool` shows system Apple/Swift dependencies and `@rpath/libNovalistSpeech.dylib`.
- `npm run package` passed and produced `app/dist/novalist-macos-arm64.dmg` (211 MB) and `app/dist/mac-arm64/Novalist.app`. The app and `Contents/Resources/backend/libNovalistSpeech.dylib` have Apple Development signatures; `codesign --verify --deep --strict` passed for the app and strict verification passed for the dylib. The app includes the audio-input entitlement and microphone/speech usage descriptions. Notarization was skipped because distribution credentials are absent.
- Real Apple SpeechTranscriber supports `en_US` and `de_DE` on this Mac. German assets were already installed; English assets were prepared through the real backend RPC. Repeating preparation for installed languages succeeded.
- Real backend → C# unmanaged callback → Swift → SpeechAnalyzer recognition passed for generated PCM16 WAVs: “The blue notebook is on the wooden table.” and “Das blaue Notizbuch liegt auf dem kleinen Tisch.” Both returned exactly once with the final word. Native transcription cancellation, immediate retry, and invalid-audio error handling passed. No extension was loaded.
- An isolated real backend copy without the dylib started successfully and returned `appleUnavailable`.
- The signed packaged backend passed the same English/German synthetic recognition, cancellation, retry, and invalid-audio checks. The signed app launched with a fresh temporary profile, no extension, and both languages ready under Settings → Writing assistance → System dictation. This is a local packaged launch, not a fresh device install or Gatekeeper/notarization acceptance.
- Automated checks passed: 16 backend dictation tests; 23 Playwright dictation tests (one optional AI Assistant test skipped because its separate assembly was not built); 3 MSBuild regression tests spanning 10 configuration/runtime combinations and the mobile opt-out; 9 packaging tests; TypeScript checking; desktop/mobile renderer builds; all 8 source checks.

Repeat the real Mac speech smoke check from the repository root:

```sh
python3 tools/apple-speech-smoke.py --prepare
# Omit --prepare to require assets to be installed already.
python3 tools/apple-speech-smoke.py --backend app/dist/mac-arm64/Novalist.app/Contents/Resources/backend/Novalist.Backend
python3 -m unittest discover -s tools -p test_apple_speech_build.py
```

`apple-speech-smoke.py` uses temporary settings, disables extensions, and generates only the two synthetic sentences with `say` (Samantha/Anna); it never records the microphone. Run with normal access to Apple's speech services: a process sandbox that denies those services can report unsupported languages even on supported hardware.

Local run logs are under ignored `artifacts/apple-speech/`, including `package.log`, `codesign-verify.log`, `packaged-speech.log`, `packaged-ui.log`, `playwright.log`, and `ios-device-build.log`.

### Still required

- Xcode 27 / .NET 10.0.401 / workload 10.0.401.1 simulator build, signed iOS/iPadOS build, device AOT linkage, install, and launch.
- Live English/German microphone accuracy and several-minute sessions; microphone denial/regrant/disconnection; background, lock, and audio-session interruptions on a physical iPhone/iPad.
- Network-disconnected recognition, cancellation/retry during a genuinely missing-asset download, and DictationTranscriber fallback on suitable hardware. Synthetic recognition while networking is available is not an offline test.
- Distribution signing/notarization and Mac App Store sandbox acceptance. The available local identities are Apple Development certificates, not Developer ID or Apple Distribution certificates.
- Optional AI Assistant MLX validation remains separate and was not attempted.
