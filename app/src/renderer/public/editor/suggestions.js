'use strict';

function newChangeId() {
    // Unique within the scene, which is all an id has to be: it names one edit
    // out of the ones in front of the author.
    return 'c' + Math.random().toString(36).slice(2, 10);
}

function suggestionAttributes(el) {
    el.setAttribute('data-nl-change', newChangeId());
    el.setAttribute('data-nl-author', window.NovalistEditorState.suggestionAuthor);
    el.setAttribute('data-nl-at', new Date().toISOString());
}

/** The <ins> the caret is already inside, if it belongs to this author. */
function currentInsertion() {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return null;
    let node = sel.getRangeAt(0).startContainer;
    if (node.nodeType === 3) node = node.parentNode;
    const ins = node && node.closest ? node.closest('ins[data-nl-change]') : null;
    if (!ins) return null;
    return ins.getAttribute('data-nl-author') === window.NovalistEditorState.suggestionAuthor ? ins : null;
}

/** Types text as a suggestion, continuing the current run where there is one. */
function insertSuggested(text) {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return;
    const range = sel.getRangeAt(0);

    // Typing over a selection is a replacement: the old words are proposed for
    // deletion rather than thrown away.
    if (!range.collapsed) markSelectionDeleted();

    const open = currentInsertion();
    if (open) {
        document.execCommand('insertText', false, text);
        return;
    }

    const ins = document.createElement('ins');
    suggestionAttributes(ins);
    ins.textContent = text;
    const at = window.getSelection().getRangeAt(0);
    at.insertNode(ins);

    const after = document.createRange();
    after.setStart(ins.firstChild, ins.firstChild.length);
    after.collapse(true);
    window.getSelection().removeAllRanges();
    window.getSelection().addRange(after);
}

/**
 * Marks whatever is selected as deleted, leaving the words in place.
 *
 * Text this author just suggested is taken back out instead of being marked:
 * proposing to delete your own unaccepted insertion is a round trip nobody
 * wants to read.
 */
function markSelectionDeleted() {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0 || sel.getRangeAt(0).collapsed) return false;

    const range = sel.getRangeAt(0);
    const contents = range.extractContents();
    const del = document.createElement('del');
    suggestionAttributes(del);

    // Drop this author's own pending insertions rather than marking them.
    contents.querySelectorAll('ins[data-nl-change]').forEach(function (ins) {
        if (ins.getAttribute('data-nl-author') !== window.NovalistEditorState.suggestionAuthor) return;
        const parent = ins.parentNode;
        while (ins.firstChild) parent.insertBefore(ins.firstChild, ins);
        parent.removeChild(ins);
    });

    del.appendChild(contents);
    if (del.textContent.length === 0) return false;

    range.insertNode(del);
    const after = document.createRange();
    after.setStartAfter(del);
    after.collapse(true);
    sel.removeAllRanges();
    sel.addRange(after);
    return true;
}

/** Extends a collapsed caret over the character a delete key would remove. */
function selectOneCharacter(backward) {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return false;
    if (!sel.getRangeAt(0).collapsed) return true;
    sel.modify('extend', backward ? 'backward' : 'forward', 'character');
    return !sel.getRangeAt(0).collapsed;
}

Object.assign(window, {
    newChangeId,
    suggestionAttributes,
    currentInsertion,
    insertSuggested,
    markSelectionDeleted,
    selectOneCharacter
});
