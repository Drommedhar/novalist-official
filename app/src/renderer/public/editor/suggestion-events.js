'use strict';

// ── Suggestion mode ─────────────────────────────────────────────
//
// With it on, typing does not change the prose - it proposes a change. New
// words go in as <ins> and deleted ones are marked <del> rather than removed,
// so the author can see what an editor is asking for and answer it a piece at
// a time. The marks live in the prose itself, so they travel with the scene.

let suggestionMode = false;

let suggestionAuthor = '';

window.setSuggestionMode = function (on, author) {
    suggestionMode = !!on;
    suggestionAuthor = author || '';
    window.NovalistEditorState.editor.classList.toggle('nv-suggesting', suggestionMode);
};

/**
 * Takes the writer to a suggested edit and says which one.
 *
 * An empty id means the first one in the scene, which is what "there are two
 * waiting in this scene" resolves to when it is followed. The marks are already
 * drawn in the prose, but a scene is long: being told a scene has edits and
 * being shown one of them are different things.
 */
window.scrollToSuggestionById = function (changeId) {
    var node = changeId
        ? window.NovalistEditorState.editor.querySelector('[data-nl-change="' + changeId + '"]')
        : window.NovalistEditorState.editor.querySelector('ins[data-nl-change], del[data-nl-change]');
    if (!node) return;
    node.scrollIntoView({ behavior: 'smooth', block: 'center' });
    node.classList.add('nv-change-flash');
    setTimeout(function () { node.classList.remove('nv-change-flash'); }, 1400);
};

window.NovalistEditorState.editor.addEventListener('beforeinput', (e) => {
    if (window.NovalistEditorState.isSettingContent || window.isCompositionInput(e) || !suggestionMode) return;

    if (e.inputType === 'insertText' && e.data) {
        e.preventDefault();
        window.insertSuggested(e.data);
        return;
    }

    if (e.inputType === 'deleteContentBackward' || e.inputType === 'deleteContentForward') {
        e.preventDefault();
        if (window.selectOneCharacter(e.inputType === 'deleteContentBackward')) window.markSelectionDeleted();
        return;
    }

    if (e.inputType === 'insertReplacementText' || e.inputType === 'insertFromPaste') {
        // A paste is many words at once; the same rule applies to all of them.
        const text = e.dataTransfer ? e.dataTransfer.getData('text/plain') : e.data;
        if (!text) return;
        e.preventDefault();
        window.insertSuggested(text);
    }
}, true);

Object.defineProperties(window.NovalistEditorState, {
    suggestionAuthor: { get() { return suggestionAuthor; }, set(value) { suggestionAuthor = value; } },
    suggestionMode: { get() { return suggestionMode; }, set(value) { suggestionMode = value; } }
});
