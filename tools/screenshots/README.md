# Screenshot pipeline

Current screenshots come from real macOS, iPhone and iPad apps using the fictional
*The Cartographer's Daughter* project. The manual uses the direct-download Mac
edition; Mac App Store images use the existing App Store edition flag so they do
not advertise extensions.

Published assets live in `docs/manual/images/` and `docs/app-store/images/`.
Capture provenance and output hashes are in `docs/app-store/screenshots.json`.
The scripts prepare images; they do not upload metadata or publish a release.

## Prerequisites

Run on macOS with Xcode and the iOS 27 simulator runtime, the repository's pinned
.NET SDK and MAUI workload, Node and the installed `app` dependencies. ImageMagick
is required for framing; `cliclick` is only needed for optional manual simulator
taps. Use dedicated screenshot simulators and isolated Electron profiles.

```sh
npm --prefix app run build
npm --prefix app run build:mobile
dotnet build Novalist.Backend/Novalist.Backend.csproj -p:NuGetAudit=false
dotnet build Novalist.Mobile/Novalist.Mobile.csproj -f net10.0-ios27.0 \
  -p:RuntimeIdentifier=iossimulator-arm64 -p:EnableCodeSigning=true \
  -p:CodesignKey=- -p:NuGetAudit=false -m:1 -nr:false
```

The simulator build is locally ad-hoc signed. No physical device, distribution
certificate or App Store upload is needed to capture screenshots.

## Generate the demo

Use a fresh scratch directory. `make-demo-project.mjs` refuses to replace an
existing project destination.

```sh
WORK="$(mktemp -d /private/tmp/novalist-screenshots.XXXXXX)"
DEMO="$WORK/Novalist/The Cartographer's Daughter"
node tools/screenshots/make-demo-project.mjs "$DEMO"
tools/screenshots/make-art.sh "$WORK/art"
node tools/screenshots/enrich-demo.mjs "$DEMO" "$WORK/art"
```

The current backend's RPC creates the project structure. The demo contains three
chapters, ten scenes, eight characters, locations, items, lore, relationships,
plotlines and timeline events. Enrichment supplies locally generated cover/banner
art, Research notes, a map, scene dates and synthetic writing history. It is
fictional fixture data, not a real author's project or performance measurement.

## Capture macOS

```sh
node tools/screenshots/capture-desktop.mjs "$DEMO" "$WORK/raw/macos"
NOVALIST_FORCE_MAS=1 node tools/screenshots/capture-desktop.mjs \
  "$DEMO" "$WORK/raw/macos-mas"
```

The script launches the actual Electron app at 1440×900 points and 2× scale,
using isolated settings and browser storage. It opens populated views, dismisses
the first-run tour, and records the captured edition/views. Normal view selection
and scrolling position the content; the application UI is not replaced or mocked.

The manual's `manuscript.png` shows the corkboard to match its existing caption.
The App Store's manuscript image shows continuous prose. The editor manual image
includes both the Context inspector and the scene-notes dock.

Raw captures preserve the window's alpha. `composite.sh` places them on a neutral
backdrop before framing, retaining translucent desktop chrome without washed-out
transparent pixels in the exported image.

## Capture iPhone and iPad

Create dedicated simulators with `xcrun simctl create`, then supply each exact
UDID. Use iPhone 17 Pro Max, iPhone 17 Pro and iPad Pro 13-inch on iOS 27 for the
current sets. The setup script touches only the selected simulator.
It restarts that simulator once to apply an English (US) native locale.

```sh
tools/screenshots/sim-setup.sh "$SHOT_UDID"
tools/screenshots/sim-seed.sh "$SHOT_UDID" "$DEMO"
```

An optional second setup argument selects an already-built `.app`. The seed
script copies the demo into the app's Documents directory, where the current app
supports local projects without a security-scoped picker grant. It derives the
recent project's ID and cover path from current metadata, preserves existing
settings/recents, and refuses to overwrite an existing demo destination.

Use native XCTest navigation or interact with the dedicated Simulator window.
The iPhone runner captures each screen after its XCTest session exits:

```sh
python3 tools/screenshots/capture-iphone.py "$IPHONE_UDID" "$WORK/raw/iphone"
python3 tools/screenshots/capture-iphone.py "$MEDIUM_IPHONE_UDID" "$WORK/raw/iphone-medium"
```

`capture-ipad.swift` contains the corresponding native iPad UI steps for a
standalone XCTest target. Run `testCaptureViews` followed by
`testDashboardFraming`, then export the named PNG attachments with
`xcrun xcresulttool export attachments`; the latter dashboard capture replaces
the initial one. Normalize their orientation before checking the landscape size.

Capture actual app screens after images, fonts and content have settled, with
onboarding dismissed and no keyboard covering the editor. Keep iPhone portrait
and iPad landscape. Setup pins the native status bar to 9:41 and full reception.
Use `xcrun simctl io "$SHOT_UDID" screenshot <file>` for the final framebuffer.

For optional manual tapping, set `NL_SIMULATOR_UDID` and `NL_WIN_RECT` (the
Simulator window's `x,y,width,height`). `tapshot.sh` saves the device framebuffer
and a separate scratch window capture. Set `NL_ROTATE` only if the framebuffer
is sideways; inspect dimensions before rotating.

Required raw filenames:

- `raw/iphone/` and `raw/iphone-medium/`: `00-welcome`, `01-dashboard`,
  `02-write`, `03-editor`, `04-codex`, `05-codex-entity`, `06-wiki`,
  `06-wiki-article`, `07-plan-menu`, `08-timeline` (all `.png`).
- `raw/ipad/`: `00-welcome`, `01-dashboard`, `02-editor`, `03-manuscript`,
  `04-timeline`, `05-relationships`, `06-codex`, `07-wiki`, `08-plotgrid`.

The dashboard capture should show actual progress metrics. Keep entity details,
wiki articles and scene prose populated. Review each frame, not just the output
file count.

## Assemble and replace the images

```sh
tools/screenshots/build-all.sh "$WORK" "$WORK/deliverables"
cp "$WORK/deliverables/Manual/"*.png docs/manual/images/
mkdir -p docs/app-store/images
cp -R "$WORK/deliverables/App Store/". docs/app-store/images/
npm --prefix app run build
npm --prefix app run build:mobile
```

The final set contains all 11 manual images and 49 App Store images. Existing
output files are regenerated without deleting unrelated files in the output
folder. The mobile manual uses separately emitted image assets, not base64 in
the entry JavaScript; rebuild and reinstall the native app when checking the
updated in-app Help on a simulator.

The store canvases match [Apple's screenshot specifications](https://developer.apple.com/help/app-store-connect/reference/app-information/screenshot-specifications),
checked on 2026-10-07:

| Folder | Pixels | Images |
| --- | --- | --- |
| macOS | 2880×1800 | 10 |
| iPhone 6.3 | 1206×2622 | 10 |
| iPhone 6.9 | 1320×2868 | 10 |
| iPhone 6.5 | 1284×2778 | 10 |
| iPad 13 | 2752×2064 | 9 |

The medium Dynamic Island iPhone set has its own native captures. The large
Dynamic Island captures also supply the optional older Face ID canvas; framing
fits the original screenshot proportionally without stretching it. All store
outputs are opaque 8-bit RGB PNGs, with at most ten per size. Retain original raw
captures separately while reviewing the framed results.
