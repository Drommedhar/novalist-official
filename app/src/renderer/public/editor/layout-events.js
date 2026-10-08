'use strict';

// ── Typewriter scroll ───────────────────────────────────────────

let pageViewEnabled = false;

let pageViewRepaginateScheduled = false;

let pageViewMutationObserver = null;

let pageViewRepaginating = false;

window.addEventListener('resize', () => {
    if (pageViewEnabled) window.schedulePageViewRepaginate();
});

// Comments and book-width changes resize the writing surface without resizing
// the frame. Watch its width too, so page breaks follow the available prose.
let pageLayoutWidth = window.NovalistEditorState.wrapper.clientWidth;
const pageLayoutObserver = new ResizeObserver(() => {
    const width = window.NovalistEditorState.wrapper.clientWidth;
    if (width === pageLayoutWidth) return;
    pageLayoutWidth = width;
    window.schedulePageViewRepaginate();
    window.scheduleCommentRender();
});
pageLayoutObserver.observe(window.NovalistEditorState.wrapper);

/**
 * Dims everything but the paragraph the caret is in.
 *
 * A class on an existing block rather than any rewriting of the prose: the
 * editor is a live document with undo, mentions and grammar decorations in it,
 * and none of them survive having their nodes replaced under them.
 */
let dimOthers = false;

// Mouse interaction pauses typewriter so drag-select works.
document.addEventListener('mousedown', (e) => {
    if (e.button === 0) window.NovalistEditorState.typewriterMouseDown = true;
    // A right- or middle-click opens a menu over the text. Recentring the caret
    // underneath it would scroll the page out from under the menu, so suspend
    // the same way a wheel scroll does.
    else window.NovalistEditorState.typewriterSuspendUntil = Date.now() + 1200;
}, true);

document.addEventListener('mouseup', () => {
    if (window.NovalistEditorState.typewriterMouseDown) {
        window.NovalistEditorState.typewriterMouseDown = false;
        // Keep suppressing for a moment so the trailing selectionchange does
        // not snap the caret line away from where the user pointed.
        window.NovalistEditorState.typewriterSuspendUntil = Date.now() + 400;
    }
}, true);

// Mouse-wheel / trackpad scroll: respect user's chosen position briefly.
window.NovalistEditorState.wrapper.addEventListener('wheel', () => {
    window.NovalistEditorState.typewriterSuspendUntil = Date.now() + 1200;
}, { passive: true });

// ── Mention picker (@) ──────────────────────────────────────────

const mentionPicker = document.getElementById('mention-picker');

// ── Auto Replacement ────────────────────────────────────────────

// How much of the line before the caret a pattern is offered. A pattern that
// backtracks badly costs time in proportion to what it is given, and this runs
// on every keystroke - a bound here is the difference between a slow rule and
// an editor that stops accepting typing.
const REGEX_LOOKBEHIND = 120;

// A rule that spends longer than this on one keystroke is set aside for the
// rest of the session. Typing cannot wait for it, and a rule that is this slow
// on one line will be slow on the next.
const REGEX_BUDGET_MS = 15;

Object.defineProperties(window.NovalistEditorState, {
    dimOthers: { get() { return dimOthers; }, set(value) { dimOthers = value; } },
    pageViewEnabled: { get() { return pageViewEnabled; }, set(value) { pageViewEnabled = value; } },
    pageViewMutationObserver: { get() { return pageViewMutationObserver; }, set(value) { pageViewMutationObserver = value; } },
    pageViewRepaginating: { get() { return pageViewRepaginating; }, set(value) { pageViewRepaginating = value; } },
    pageViewRepaginateScheduled: { get() { return pageViewRepaginateScheduled; }, set(value) { pageViewRepaginateScheduled = value; } },
    mentionPicker: { get() { return mentionPicker; } },
    REGEX_LOOKBEHIND: { get() { return REGEX_LOOKBEHIND; } },
    REGEX_BUDGET_MS: { get() { return REGEX_BUDGET_MS; } }
});
