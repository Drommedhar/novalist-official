# Screenshot pipeline

Reproducible App Store and user-manual screenshots for all three targets: macOS,
iPhone and iPad. Everything is shot from the real apps against a generated demo
project, so no personal writing ever appears in a published image.

macOS only — it drives Electron through Playwright, the iOS Simulator through
`simctl`, and composites with ImageMagick.

## Why the demo project exists

Screenshots of a real project leak the author's book, and screenshots of an empty
project make every view look broken. `demo-content.mjs` is the single source of
truth for a fictional novel — *The Cartographer's Daughter* — sized so that every
view has something to show: three chapters over three acts, ten scenes of prose,
eight characters with relationships, six locations, four items, three lore
entries, four plotlines, twelve timeline events, and story dates that put scenes
on the Calendar.

## Prerequisites

- `npm --prefix app run build` (the capture scripts launch `app/out/main/index.js`)
- a debug build of `Novalist.Backend` — the dev backend path the main process resolves
- `brew install imagemagick` and `brew install cliclick` (the latter only for the
  iOS captures, which drive the Simulator by clicking it)
- for iOS: full Xcode, the `maui-ios` workload, and
  `dotnet build Novalist.Mobile/Novalist.Mobile.csproj -f net10.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64`

## macOS

```sh
WORK=~/nl-shots
node tools/screenshots/make-demo-project.mjs "$WORK/Novalist/The Cartographer's Daughter"
tools/screenshots/make-art.sh "$WORK/art"
node tools/screenshots/enrich-demo.mjs "$WORK/Novalist/The Cartographer's Daughter" "$WORK/art"
node tools/screenshots/capture-desktop.mjs "$WORK/Novalist/The Cartographer's Daughter" "$WORK/raw/macos"
```

`make-art.sh` generates the demo book's cover and banner (abstract bathymetric
contours in the app's palette) so the Dashboard and welcome screen are not full
of empty image placeholders.

### The Liquid Glass transparency fix

On macOS 26 the app launches its window with `transparent: true` so the native
`NSGlassEffectView` can show through the chrome — see `app/src/main/glass.ts`. A
Playwright screenshot captures only the web layer, so the chrome comes out at
60–72% alpha with nothing behind it, which reads as washed-out grey.

`composite.sh` flattens each capture onto a neutral desktop backdrop. That is
what the user actually sees when the window sits on a plain wallpaper: the chrome
stays translucent, but it now has something to be translucent against. Raw
captures deliberately keep their alpha so this step can be re-tuned without
re-shooting.

## iOS

On iOS a project folder can only be reached through a security-scoped bookmark,
and only the system document picker can mint one — so the demo project cannot
just be copied into the app container. The flow works around that without any
app change:

1. `tools/screenshots/sim-setup.sh "iPhone 17 Pro Max"` — boots, installs, and
   pins the status bar to 9:41 with full bars
2. in the app, tap **Browse for Project Folder…** and pick **On My iPhone**
   (navigate up if the picker opens inside another folder). This stores a
   bookmark for that folder.
3. `tools/screenshots/sim-seed.sh <udid> <demo-project-dir>` — copies the demo
   project into the bookmarked folder and writes a recents entry pointing at it,
   then relaunches

Step 3 works because `SecurityScopedFolders.BeginAccess` walks ancestors, so the
bookmark on the parent authorises the project subfolder. `sim-seed.sh` replaces
`settings.json` wholesale, which also clears any real projects from that
simulator's recents — worth knowing if you use the same simulator for
development.

From there, `tapshot.sh` taps a screen coordinate and saves both a clean device
screenshot (the deliverable) and a capture of the Simulator window, which is what
you read to work out the next tap:

```sh
export NL_WIN_RECT=766,33,455,974   # from: osascript -e 'tell application "System Events" \
                                    #   to tell process "Simulator" to get {position, size} of window 1'
export NL_ROTATE=-90                # landscape iPad only; simctl writes the framebuffer unrotated
tools/screenshots/tapshot.sh 924 938 "$WORK/raw/iphone/02-write.png"
```

iPad screenshots are taken in landscape (rotate with Cmd+Left) so the two-pane
layout is visible.

## Assembling

```sh
tools/screenshots/build-all.sh "$WORK" ~/Desktop/Novalist-Screenshots
```

Writes upload-ready store screenshots at Apple's exact sizes — macOS
2880×1800, iPhone 6.9" 1320×2868, iPhone 6.5" 1284×2778, iPad 13" 2752×2064
landscape — each framed by `frame.sh` with a headline over a gradient drawn from
the app's colour tokens.

Both phone classes come from the same captures. Because the device screenshot is
inset in the frame rather than full-bleed, a new phone aspect ratio only needs a
re-run of the framing step — never a re-shoot, and nothing is ever stretched.

It also writes the 1440×900 manual set; copy that into place:

```sh
cp ~/Desktop/Novalist-Screenshots/Manual/*.png docs/manual/images/
npm --prefix app run build:mobile   # the mobile bundle inlines these as base64
```

That last step matters: `app/vite.mobile.config.ts` embeds `docs/manual/images/`
into the mobile bundle for the in-app manual, so refreshed screenshots need a
bundle rebuild to reach the phone.

## Gotchas

- ImageMagick: screening an opaque `radial-gradient:` over a background swamps
  the whole canvas instead of tinting it. Both the cover art and the store frames
  avoid it.
- Rounding corners needs `-compose DstIn` against a white rounded-rect mask.
  `copyopacity` on an `-alpha off` source silently does nothing, leaving a square
  image whose drop shadow then swallows it.
- `magick identify -format '%w %h'` emits no trailing newline, so `read` exits
  non-zero and kills a `set -e` script. Use `'%w %h\n'`.
