'use strict';

// ── Event Handlers ──────────────────────────────────────────────

let contentChangeTimer = null;

// The base URL the display form of an image path hangs off. Scene HTML
// stores a book-relative path so the project stays portable; the editor is a
// document of its own and needs a real URL to show anything.
let imageBaseUrl = '';

/**
 * The paragraph the writer asked for an image at, remembered from the moment of
 * asking.
 *
 * The frame owns no file picker, so inserting means a round trip through the
 * host: a native file dialog, then a dialog asking for alt text. Both take focus
 * out of this frame and leave it with no selection at all, so by the time the
 * path comes back the caret is long gone and reading it then put every image at
 * the end of the scene.
 */
let imageTargetBlock = null;

window.NovalistEditorState.editor.addEventListener('compositionstart', () => {
    window.NovalistEditorState.isComposing = true;
    if (window.NovalistEditorState.compositionEndTimer) clearTimeout(window.NovalistEditorState.compositionEndTimer);
    window.NovalistEditorState.compositionEndTimer = null;
    if (contentChangeTimer) clearTimeout(contentChangeTimer);
    contentChangeTimer = null;
    if (window.NovalistEditorState.dialogueCorrectionTimer) clearTimeout(window.NovalistEditorState.dialogueCorrectionTimer);
    window.NovalistEditorState.dialogueCorrectionTimer = null;
    if (window.NovalistEditorState.readabilityTimer) clearTimeout(window.NovalistEditorState.readabilityTimer);
    window.NovalistEditorState.readabilityTimer = null;
    if (window.NovalistEditorState.grammarCheckTimer) clearTimeout(window.NovalistEditorState.grammarCheckTimer);
    window.NovalistEditorState.grammarCheckTimer = null;
    window.NovalistEditorState.grammarRequestId++;
    window.NovalistEditorState.pendingGrammarRequest = null;
    window.NovalistEditorState.grammarChecking = false;
    window.updateGrammarStatusBar();
    if (window.NovalistEditorState.readAloudActive) window.stopReadAloud();
});

window.NovalistEditorState.editor.addEventListener('compositionend', () => {
    window.NovalistEditorState.isComposing = false;
    // Some engines send their final input after compositionend. Wait until
    // that edit has landed before normalizing or moving any of its nodes.
    window.NovalistEditorState.compositionEndTimer = setTimeout(() => {
        window.NovalistEditorState.compositionEndTimer = null;
        if (window.NovalistEditorState.isComposing || window.NovalistEditorState.isSettingContent) return;
        window.NovalistEditorState.grammarIssues = [];
        window.applyGrammarHighlights();
        window.updateGrammarStatusBar();
        window.handleEditorInput();
        window.schedulePageViewRepaginate();
    }, 0);
});

window.NovalistEditorState.editor.addEventListener('input', window.handleEditorInput);

window.NovalistEditorState.editor.addEventListener('beforeinput', (e) => {
    if (window.NovalistEditorState.isSettingContent || window.isCompositionInput(e)) return;
    if (e.inputType !== 'insertText' || !e.data) return;

    const result = window.tryAutoReplace(e.data);
    if (result && result.replacement !== e.data) {
        e.preventDefault();

        if (result.backtrack > 0) {
            const sel = window.getSelection();
            if (sel && sel.rangeCount > 0) {
                for (let i = 0; i < result.backtrack; i++) {
                    sel.modify('extend', 'backward', 'character');
                }
            }
        }

        document.execCommand('insertText', false, result.replacement);
    }
});

window.NovalistEditorState.editor.addEventListener('keydown', (e) => {
    if (window.isCompositionInput(e)) return;
    if (window.NovalistEditorState.slashState) {
        if (e.key === 'Escape') { window.closeSlashMenu(); e.preventDefault(); e.stopPropagation(); return; }
        if (e.key === 'ArrowDown') { window.moveSlashSelection(1); e.preventDefault(); e.stopPropagation(); return; }
        if (e.key === 'ArrowUp') { window.moveSlashSelection(-1); e.preventDefault(); e.stopPropagation(); return; }
        if (e.key === 'Enter' || e.key === 'Tab') {
            window.confirmSlashAt(window.NovalistEditorState.slashSelectedIndex);
            e.preventDefault();
            e.stopPropagation();
            return;
        }
    }
    if (!window.NovalistEditorState.mentionState) return;
    if (e.key === 'Escape') {
        e.preventDefault();
        e.stopPropagation();
        window.closeMentionPicker(true);
        return;
    }
    if (e.key === 'ArrowDown') {
        e.preventDefault();
        e.stopPropagation();
        window.moveMentionSelection(1);
        return;
    }
    if (e.key === 'ArrowUp') {
        e.preventDefault();
        e.stopPropagation();
        window.moveMentionSelection(-1);
        return;
    }
    if (e.key === 'Enter' || e.key === 'Tab') {
        // The "Create <name>" row counts as a confirmable entry too.
        if (window.NovalistEditorState.mentionFiltered.length > 0 || window.NovalistEditorState.mentionCreateVisible) {
            e.preventDefault();
            e.stopPropagation();
            window.confirmMentionAt(window.NovalistEditorState.mentionSelectedIndex);
            return;
        }
        // Nothing to confirm — Enter inserts a newline and closes the picker.
        window.closeMentionPicker(true);
        return;
    }
}, true);

window.NovalistEditorState.editor.addEventListener('input', (e) => {
    if (window.NovalistEditorState.isSettingContent || window.isCompositionInput(e)) return;

    if (window.NovalistEditorState.mentionState) {
        window.updateMentionQuery();
        return;
    }
    // After a normal input event, check if caret just landed right after a
    // freshly-typed `@` that should open the picker.
    if (window.detectMentionAtCaret()) {
        window.openMentionPicker();
        return;
    }
    // Slash commands follow the same hooks as the mention picker: detect on
    // input, filter as the writer types, close when the caret leaves.
    if (window.NovalistEditorState.slashState) window.updateSlashQuery();
    else if (window.detectSlashAtCaret()) window.openSlashMenu();
    // The book's own completion list, on the same hook. Closed while a mention
    // or a slash command is open: two popups over one caret is a fight.
    if (!window.NovalistEditorState.mentionState && !window.NovalistEditorState.slashState) window.updateCompletion();
    else window.closeCompletion();
});

window.NovalistEditorState.editor.addEventListener('keydown', (e) => {
    if (window.isCompositionInput(e)) return;
    if (!window.NovalistEditorState.completionState || window.NovalistEditorState.completionMatches.length === 0) return;
    if (e.key === 'Escape') { e.preventDefault(); window.closeCompletion(); return; }
    if (e.key === 'Tab') {
        e.preventDefault();
        window.acceptCompletion(window.NovalistEditorState.completionIndex);
        return;
    }
    if (e.key === 'ArrowDown') {
        e.preventDefault();
        window.NovalistEditorState.completionIndex = (window.NovalistEditorState.completionIndex + 1) % window.NovalistEditorState.completionMatches.length;
        window.renderCompletion();
        return;
    }
    if (e.key === 'ArrowUp') {
        e.preventDefault();
        window.NovalistEditorState.completionIndex =
            (window.NovalistEditorState.completionIndex - 1 + window.NovalistEditorState.completionMatches.length) % window.NovalistEditorState.completionMatches.length;
        window.renderCompletion();
    }
    // Enter is not handled on purpose: in prose it starts a paragraph, and a
    // popup that swallows it is worse than having no completion at all.
}, true);

document.addEventListener('selectionchange', () => {
    if (window.NovalistEditorState.isComposing) return;
    if (window.NovalistEditorState.dimOthers) window.updateFocusBlock();
    if (window.NovalistEditorState.isSettingContent) return;
    window.notifyFormattingChanged();
    const pos = window.getCaretPosition();
    window.sendMessage({ type: 'caretPosition', line: pos.line, column: pos.column });
    // Close mention picker if caret leaves the pending span.
    if (window.NovalistEditorState.mentionState) {
        const sel = window.getSelection();
        if (sel && sel.rangeCount > 0) {
            const r = sel.getRangeAt(0);
            if (window.NovalistEditorState.mentionState.node !== r.startContainer) {
                window.closeMentionPicker(true);
            }
        }
    }
});

// Follow deliberate keyboard movement, not selectionchange: decoration and
// pagination also restore selections, including a caret the writer scrolled
// away from while reading. Those background updates must leave the view alone.
window.NovalistEditorState.editor.addEventListener('keydown', (e) => {
    if (window.isCompositionInput(e)) return;
    if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown'].includes(e.key)) {
        window.scheduleTypewriterRecenter();
    }
});

Object.defineProperties(window.NovalistEditorState, {
    imageBaseUrl: { get() { return imageBaseUrl; }, set(value) { imageBaseUrl = value; } },
    imageTargetBlock: { get() { return imageTargetBlock; }, set(value) { imageTargetBlock = value; } },
    contentChangeTimer: { get() { return contentChangeTimer; }, set(value) { contentChangeTimer = value; } }
});
