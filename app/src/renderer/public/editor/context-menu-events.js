'use strict';

// ── Custom Context Menu ─────────────────────────────────────────

const contextMenu = document.getElementById('context-menu');

// Selection captured when the menu opened (see the contextmenu handler).
let contextMenuRange = null;

let contextMenuText = '';

let contextSpellingPoint = null;

let contextSpellingTarget = null;

// Same trick the floating toolbar uses: swallowing mousedown keeps focus — and
// therefore the selection — inside the editor while a menu row is clicked.
contextMenu.addEventListener('mousedown', (e) => e.preventDefault());

let contextMenuLabels = { cut: 'Cut', copy: 'Copy', paste: 'Paste', selectAll: 'Select All' };

document.addEventListener('contextmenu', (e) => {
    // Deliberately NOT preventDefault: Electron shows no menu of its own, and
    // preventing the default stops the context-menu event ever reaching the
    // main process - which is the only place Chromium's spelling suggestions
    // exist. Suppressing it is what made a red underline unanswerable.
    e.stopPropagation();
    window.hideGrammarPopup();

    const sel = window.getSelection();
    const hasSelection = sel && !sel.isCollapsed;

    // Snapshot the selection now. Clicking a menu row moves focus out of the
    // contenteditable, which collapses the live selection before the click
    // handler runs — so actions that act on the selection must use this copy.
    // With nothing selected the caret itself is the anchor: an action that
    // allows an empty selection still has to know where to write.
    contextMenuRange = sel && sel.rangeCount > 0 ? sel.getRangeAt(0).cloneRange() : null;
    contextMenuText = hasSelection ? sel.toString() : '';
    contextSpellingTarget = null;
    contextSpellingPoint = null;
    // A right-click need not move the selection (notably on Windows). Capture
    // the point in the prose, independently of where the caret was parked.
    const hit = document.caretRangeFromPoint(e.clientX, e.clientY);
    if (hit && window.NovalistEditorState.editor.contains(hit.startContainer)) {
        const block = window.getContainingBlock(hit.startContainer) || window.NovalistEditorState.editor;
        const before = document.createRange();
        before.selectNodeContents(block);
        before.setEnd(hit.startContainer, hit.startOffset);
        contextSpellingPoint = { block, offset: before.toString().length, text: block.textContent };
    }

    let html = '';

    html += window.grammarContextMenuHtml(e);
    html += window.editingContextMenuHtml(hasSelection);
    html += window.entityContextMenuHtml(hasSelection);
    html += window.extensionContextMenuHtml(hasSelection);

    contextMenu.replaceChildren(window.sanitizeSceneFragment(html));

    window.positionContextMenu(e, contextMenu);
});

contextMenu.addEventListener('click', (e) => {
    const suggestion = e.target.closest('.cm-suggestion');
    if (suggestion && suggestion.hasAttribute('data-spelling')) {
        const target = contextSpellingTarget;
        if (target && window.NovalistEditorState.editor.contains(target.block) && target.block.textContent === target.text) {
            // Underline refreshes can split/merge the text nodes while the
            // menu is open. Resolve the saved block offset against those nodes.
            const from = window.textPointInElement(target.block, target.start);
            const to = window.textPointInElement(target.block, target.start + target.word.length);
            const range = document.createRange();
            range.setStart(from.node, from.offset);
            range.setEnd(to.node, to.offset);
            window.replaceProofingRange(range, target.word, suggestion.getAttribute('data-spelling'));
        }
        window.hideContextMenu();
        return;
    }
    if (suggestion) {
        const index = parseInt(suggestion.getAttribute('data-issue-index'), 10);
        const replacement = suggestion.getAttribute('data-replacement');
        window.applyGrammarSuggestion(index, replacement);
        window.hideContextMenu();
        return;
    }

    const inlineItem = e.target.closest('[data-inline-action]');
    if (inlineItem) {
        const id = inlineItem.getAttribute('data-inline-action');
        window.triggerInlineAction(id);
        window.hideContextMenu();
        return;
    }

    const extItem = e.target.closest('[data-ext-ctx]');
    if (extItem) {
        window.sendMessage({ type: 'extensionContextMenuRequested', itemId: extItem.getAttribute('data-ext-ctx') });
        window.hideContextMenu();
        return;
    }

    const item = e.target.closest('.cm-item');
    if (!item || item.classList.contains('disabled')) return;

    const action = item.getAttribute('data-action');
    if (action === 'addToDictionary') {
        window.sendMessage({ type: 'addToDictionary', word: item.getAttribute('data-word') });
    } else {
        window.applyContextAction(action);
    }
    window.hideContextMenu();
});

/**
 * Host entry point for the same actions. The palette takes focus on its way
 * open, so the caret comes back to the editor before anything acts on it.
 */
window.runContextAction = function (action) {
    window.NovalistEditorState.editor.focus();
    window.applyContextAction(action);
};

document.addEventListener('click', (e) => {
    if (!contextMenu.contains(e.target)) window.hideContextMenu();
});

document.addEventListener('keydown', (e) => {
    if (window.isCompositionInput(e)) return;
    if (e.key === 'Escape') {
        window.hideContextMenu();
        window.stopReadAloud();
    }
});

// Only a scroll the *user* made dismisses the menu. Caret recentring scrolls
// the wrapper too, and letting that through closed the menu in the same frame
// it opened.
window.NovalistEditorState.wrapper.addEventListener('scroll', () => {
    if (Date.now() < window.NovalistEditorState.programmaticScrollUntil) return;
    window.hideContextMenu();
});

Object.defineProperties(window.NovalistEditorState, {
    contextMenuRange: { get() { return contextMenuRange; }, set(value) { contextMenuRange = value; } },
    contextMenuText: { get() { return contextMenuText; }, set(value) { contextMenuText = value; } },
    contextMenuLabels: { get() { return contextMenuLabels; }, set(value) { contextMenuLabels = value; } },
    contextMenu: { get() { return contextMenu; } },
    contextSpellingPoint: { get() { return contextSpellingPoint; }, set(value) { contextSpellingPoint = value; } },
    contextSpellingTarget: { get() { return contextSpellingTarget; }, set(value) { contextSpellingTarget = value; } }
});
