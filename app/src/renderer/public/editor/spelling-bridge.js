'use strict';

/**
 * Chromium's spelling suggestions for the word the menu was opened on.
 *
 * They arrive just after the menu is drawn, because they come from the main
 * process with the context-menu event. Folded into the top of the menu that is
 * already on screen rather than opening a second one beside it.
 */
window.setSpellingSuggestions = function (word, suggestions) {
    if (!window.NovalistEditorState.contextMenu || !window.NovalistEditorState.contextMenu.classList.contains('visible')) return;
    if (!word) return;
    window.NovalistEditorState.contextSpellingTarget = null;
    const point = window.NovalistEditorState.contextSpellingPoint;
    if (point && window.NovalistEditorState.editor.contains(point.block) && point.block.textContent === point.text) {
        // Match only the occurrence containing the original right-click, never
        // the first matching word in the scene or the current caret position.
        let start = point.text.indexOf(word);
        while (start !== -1) {
            if (start <= point.offset && point.offset <= start + word.length
                && !window.isWordChar(point.text[start - 1]) && !window.isWordChar(point.text[start + word.length])) {
                window.NovalistEditorState.contextSpellingTarget = { block: point.block, start, text: point.text, word };
                break;
            }
            start = point.text.indexOf(word, start + 1);
        }
    }

    // Chromium can report the same menu more than once - a second listener, a
    // re-send - and the block was being prepended each time, so the writer saw
    // every suggestion twice. Whatever was injected before goes first.
    for (const stale of window.NovalistEditorState.contextMenu.querySelectorAll('.cm-spelling')) stale.remove();

    const list = Array.isArray(suggestions) ? suggestions : [];
    const rows = [];
    rows.push('<div class="cm-grammar-msg cm-spelling">' + window.escapeHtml(word) + '</div>');
    if (list.length === 0) {
        rows.push('<div class="cm-item cm-spelling" disabled><span>'
            + window.escapeHtml(window.NovalistEditorState.contextMenuLabels.noSuggestions || 'No suggestions')
            + '</span></div>');
    } else {
        for (const s of list) {
            rows.push('<div class="cm-suggestion cm-spelling" data-spelling="'
                + window.escapeAttr(s) + '">' + window.escapeHtml(s) + '</div>');
        }
    }
    rows.push('<div class="cm-item cm-spelling" data-action="addToDictionary" data-word="'
        + window.escapeAttr(word) + '"><span>'
        + window.escapeHtml(window.NovalistEditorState.contextMenuLabels.addToDictionary || 'Add to Dictionary')
        + '</span></div>');
    rows.push('<div class="cm-separator"></div>');

    window.NovalistEditorState.contextMenu.insertAdjacentHTML('afterbegin', rows.join(''));
};
