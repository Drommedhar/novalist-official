#!/usr/bin/env bash
#
# Turns the raw captures into the two deliverables:
#   1. App Store screenshots for macOS, iPhone and iPad, at Apple's exact sizes
#   2. replacement manual screenshots with the Liquid Glass transparency fixed
#
# <work-dir> is the directory the capture scripts wrote raw/{macos,iphone,ipad}
# into; intermediates are kept there so a re-run only redoes the cheap steps.
#
# Usage: build-all.sh <work-dir> <out-dir>
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="${1:?usage: build-all.sh <work-dir> <out-dir>}"
OUT="${2:?usage: build-all.sh <work-dir> <out-dir>}"
mkdir -p "$OUT/App Store/macOS" "$OUT/App Store/iPhone 6.9" "$OUT/App Store/iPhone 6.5" \
         "$OUT/App Store/iPhone 6.3" "$OUT/App Store/iPad 13" "$OUT/Manual"

# --- macOS: flatten the glass alpha first, everything downstream reuses it ----
echo "compositing macOS captures..."
FLAT="$WORK/flat/macos"
"$HERE/composite.sh" "$WORK/raw/macos" "$FLAT" 1440 900 >/dev/null
STORE_FLAT="$WORK/flat/macos-mas"
"$HERE/composite.sh" "$WORK/raw/macos-mas" "$STORE_FLAT" >/dev/null

# --- 1. App Store: macOS (2880x1800) -----------------------------------------
echo "framing macOS store screenshots..."
mac() { "$HERE/frame.sh" "$STORE_FLAT/$2.png" "$OUT/App Store/macOS/$1-$2.png" 2880 1800 "$3" "$4"; }
mac 01 interface-overview "Everything your book needs, in one window" \
    "Binder, editor and live story context, side by side"
mac 02 dashboard "See the shape of your progress" \
    "Word goals, streaks and reading time, kept as you write"
mac 03 manuscript "Read the whole book, straight through" \
    "Every chapter and scene compiled while you work"
mac 04 corkboard "Rearrange the story on a corkboard" \
    "Drag scenes into place, synopsis first"
mac 05 codex "A codex that remembers your world" \
    "Characters, locations, items and lore in one place"
mac 06 wiki "Your story bible, written for you" \
    "Every entity cross-linked into a wiki you can browse"
mac 07 timeline "Every event on one timeline" \
    "Backstory and plot, in the order they actually happen"
mac 08 plot-grid "Track every thread, scene by scene" \
    "See at a glance where a plotline goes quiet"
mac 09 relationships "See how everyone connects" \
    "A relationship graph built straight from the Codex"
mac 10 command-palette "Every command, one keystroke away" \
    "Jump to any view, scene or entity without leaving the keyboard"

# --- 2. App Store: iPhone ----------------------------------------------------
# Rendered for the current medium Dynamic Island class (1206x2622), plus the
# existing large Dynamic Island (1320x2868) and large Face ID (1284x2778) sets.
# The artwork is inset rather than full-bleed, so the different
# aspect ratios need no re-shoot and nothing is stretched - only the frame
# around the device capture changes.
echo "framing iPhone store screenshots..."
phone_shots() { # <dest-subdir> <canvas-w> <canvas-h> <capture-subdir>
  local dir="$1" w="$2" h="$3" capture_dir="$4"
  ip() { "$HERE/frame.sh" "$WORK/raw/$capture_dir/$2.png" "$OUT/App Store/$dir/$1.png" "$w" "$h" "$3" "$4"; }
  ip 01 03-editor "Write anywhere" "The same editor, sized for one hand"
  ip 02 01-dashboard "See the shape of your progress" "Writing goals and daily progress"
  ip 03 02-write "Your whole outline, one tap away" "Acts, chapters and scenes"
  ip 04 04-codex "A codex that remembers your world" "Characters, locations, items and lore"
  ip 05 05-codex-entity "Every detail where you left it" "Full entity sheets on the phone"
  ip 06 06-wiki-article "Your story bible, written for you" "Cross-linked from your own notes"
  ip 07 08-timeline "Every event on one timeline" "Backstory and plot in order"
  ip 08 07-plan-menu "Plan on the move" "Timeline, plot grid and calendar"
  ip 09 06-wiki "Browse the whole world" "Every entity, one search away"
  ip 10 00-welcome "Pick up where you left off" "The same project as on your Mac"
}
phone_shots "iPhone 6.9" 1320 2868 iphone
phone_shots "iPhone 6.5" 1284 2778 iphone
phone_shots "iPhone 6.3" 1206 2622 iphone-medium

# --- 3. App Store: iPad 13" landscape (2752x2064) ----------------------------
echo "framing iPad store screenshots..."
pad() { "$HERE/frame.sh" "$WORK/raw/ipad/$2.png" "$OUT/App Store/iPad 13/$1.png" 2752 2064 "$3" "$4"; }
pad 01 02-editor "Write with the outline beside you" \
    "A two-pane layout built for the iPad"
pad 02 01-dashboard "See the shape of your progress" \
    "Word goals, streaks and reading time"
pad 03 03-manuscript "Read the whole book, straight through" \
    "Every chapter compiled as you write"
pad 04 06-codex "A codex that remembers your world" \
    "Characters, locations, items and lore in one place"
pad 05 07-wiki "Your story bible, written for you" \
    "Every entity cross-linked into a browsable wiki"
pad 06 04-timeline "Every event on one timeline" \
    "Backstory and plot, in the order they happen"
pad 07 05-relationships "See how everyone connects" \
    "A relationship graph built from the Codex"
pad 08 08-plotgrid "Track every thread, scene by scene" \
    "See at a glance where a plotline goes quiet"
pad 09 00-welcome "Pick up where you left off" \
    "The same project as on your Mac"

# --- 4. Manual screenshots (1440x900, transparency fixed) --------------------
echo "building manual screenshots..."
for name in calendar codex command-palette dashboard editor interface-overview \
            manuscript plot-grid relationships start-screen timeline; do
  cp "$FLAT/$name.png" "$OUT/Manual/$name.png"
done

echo
echo "done:"
find "$OUT" -name '*.png' | wc -l | xargs echo "  images:"
du -sh "$OUT" | cut -f1 | xargs echo "  size:  "
