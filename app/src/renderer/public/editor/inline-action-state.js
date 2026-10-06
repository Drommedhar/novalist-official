'use strict';

// ── Inline actions (extension-contributed) ──────────────────────
let inlineActions = [];

let pendingInlineAction = null;

// { id, range, originalText, disposition }

window.setInlineActions = function (json) {
    try {
        inlineActions = typeof json === 'string' ? JSON.parse(json) : (json || []);
        console.log('[InlineActions] setInlineActions received', inlineActions.length, 'items', inlineActions);
    } catch (e) {
        console.log('[InlineActions] setInlineActions parse error', e);
        inlineActions = [];
    }
};

// The prose before a point, capped so a long scene does not push a whole
// chapter through the RPC. This is what a continue-writing action continues
// from, and at a bare caret it is the only context there is.
const PRECEDING_TEXT_LIMIT = 4000;

// ── Slash commands ──────────────────────────────────────────────
//
// A slash at the start of a line opens a menu of the inline actions that work
// with nothing selected. This is the surface a "continue writing" or a typed
// beat directive needs: there is no selection to right-click, so the context
// menu cannot reach them.
//
// Mirrors the @-mention picker's shape (detect at caret, filter as you type,
// arrow/enter to commit) because the writer has already learned that gesture.

/* ===== The book's own completion list =====
 *
 * The @-mention picker completes Codex names, in scene prose, and nothing else.
 * That leaves out everything a secondary world is full of and the Codex is not:
 * a settled spelling of a place, a rank, a coined verb, a phrase that has to
 * read the same way every time. Those get retyped slightly differently, and the
 * inconsistency turns up in copy-edit.
 *
 * Deliberately quieter than the mention picker: it never steals Enter, because
 * Enter in prose means a new paragraph and a completion popup that swallows it
 * is worse than no completion at all. Tab accepts.
 */
let completionWords = [];

let completionTrigger = 3;

let completionState = null;

// { node, start, query }
let completionMatches = [];

let completionIndex = 0;

let slashState = null;

// { node, atOffset, query }
let slashFiltered = [];

let slashSelectedIndex = 0;

// Extension context-menu items (IContextMenuContributor): not selection-gated;
// the host runs the click handler against the currently open scene.
let extensionMenuItems = [];

window.setExtensionContextMenuItems = function (json) {
    try {
        extensionMenuItems = typeof json === 'string' ? JSON.parse(json) : (json || []);
    } catch {
        extensionMenuItems = [];
    }
};

Object.defineProperties(window.NovalistEditorState, {
    inlineActions: { get() { return inlineActions; }, set(value) { inlineActions = value; } },
    PRECEDING_TEXT_LIMIT: { get() { return PRECEDING_TEXT_LIMIT; } },
    pendingInlineAction: { get() { return pendingInlineAction; }, set(value) { pendingInlineAction = value; } },
    completionWords: { get() { return completionWords; }, set(value) { completionWords = value; } },
    completionTrigger: { get() { return completionTrigger; }, set(value) { completionTrigger = value; } },
    completionMatches: { get() { return completionMatches; }, set(value) { completionMatches = value; } },
    completionState: { get() { return completionState; }, set(value) { completionState = value; } },
    completionIndex: { get() { return completionIndex; }, set(value) { completionIndex = value; } },
    slashState: { get() { return slashState; }, set(value) { slashState = value; } },
    slashSelectedIndex: { get() { return slashSelectedIndex; }, set(value) { slashSelectedIndex = value; } },
    slashFiltered: { get() { return slashFiltered; }, set(value) { slashFiltered = value; } },
    extensionMenuItems: { get() { return extensionMenuItems; }, set(value) { extensionMenuItems = value; } }
});
