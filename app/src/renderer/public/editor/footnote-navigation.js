'use strict';

window.scrollToFootnoteById = function (footnoteId) {
    var node = window.NovalistEditorState.editor.querySelector('sup.nv-fn[data-fn-id="' + footnoteId + '"]');
    if (!node) return;
    node.scrollIntoView({ behavior: 'smooth', block: 'center' });
    var prev = node.style.background;
    node.style.background = 'rgba(137, 180, 250, 0.4)';
    setTimeout(function () { node.style.background = prev || ''; }, 1200);
};

window.removeFootnoteById = function (footnoteId) {
    var nodes = window.NovalistEditorState.editor.querySelectorAll('sup.nv-fn[data-fn-id="' + footnoteId + '"]');
    nodes.forEach(function (n) { n.remove(); });
    var ids = window.renumberFootnotes();
    try { window.NovalistEditorState.editor.dispatchEvent(new Event('input', { bubbles: true })); } catch (error) { console.warn("editor: operation failed", error instanceof Error ? error.name : typeof error); }
    // Taking one out moves every marker after it up a number, so the list is
    // reported again rather than only the removal.
    window.sendMessage({ type: 'footnotesRenumbered', ids: ids });
};

window.NovalistEditorState.editor.addEventListener('click', function (e) {
    var target = e.target;
    if (target && target.classList && target.classList.contains('nv-fn')) {
        var id = target.getAttribute('data-fn-id');
        if (id) window.sendMessage({ type: 'footnoteClicked', footnoteId: id });
    }
});

// ── Split at the caret ──────────────────────────────────────────
//
// Returns the document either side of the caret so the host can turn one scene
// into two. The split happens at block granularity: the block the caret sits in
// is divided at the caret, and whole blocks fall to one side or the other. That
// keeps both halves valid markup, which splitting the raw HTML string at a
// character offset would not.

window.splitAtCaret = function () {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return null;

    const caret = sel.getRangeAt(0);
    if (!window.NovalistEditorState.editor.contains(caret.startContainer)) return null;

    const before = document.createRange();
    before.setStart(window.NovalistEditorState.editor, 0);
    before.setEnd(caret.startContainer, caret.startOffset);

    const after = document.createRange();
    after.setStart(caret.startContainer, caret.startOffset);
    after.setEnd(window.NovalistEditorState.editor, window.NovalistEditorState.editor.childNodes.length);

    const wrap = function (fragment) {
        const holder = document.createElement('div');
        holder.appendChild(fragment);
        // A caret mid-paragraph leaves each side holding a partial block; the
        // browser closes the tags for us when the fragment is cloned.
        return holder.innerHTML;
    };

    const beforeHtml = wrap(before.cloneContents());
    const afterHtml = wrap(after.cloneContents());

    // Splitting at the very start or the very end would leave one scene empty,
    // which is never what the writer meant.
    if (beforeHtml.replace(/<[^>]*>/g, '').trim().length === 0) return null;
    if (afterHtml.replace(/<[^>]*>/g, '').trim().length === 0) return null;

    return JSON.stringify({ before: beforeHtml, after: afterHtml });
};
