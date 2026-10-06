'use strict';

// ── Inline Comments ─────────────────────────────────────────────

function wrapSelectionAsComment(commentId) {
    var sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return null;
    var range = sel.getRangeAt(0);
    if (range.collapsed) return null;
    var anchorText = sel.toString();
    if (!anchorText) return null;

    var span = document.createElement('span');
    span.className = 'nv-comment';
    span.setAttribute('data-comment-id', commentId);
    try {
        range.surroundContents(span);
    } catch {
        try {
            var frag = range.extractContents();
            span.appendChild(frag);
            range.insertNode(span);
        } catch {
            return null;
        }
    }
    sel.removeAllRanges();
    // Notify host that the editor HTML changed so the comment span persists.
    try { window.NovalistEditorState.editor.dispatchEvent(new Event('input', { bubbles: true })); } catch (error) { console.warn("editor: wrapSelectionAsComment failed", error instanceof Error ? error.name : typeof error); }
    return anchorText;
}

function clearCommentMark(span) {
    span.classList.remove('nv-comment', 'nv-comment-active');
    if (!span.className) span.removeAttribute('class');
    span.removeAttribute('data-comment-id');
}

function removeCommentSpan(commentId) {
    window.NovalistEditorState.editor.querySelectorAll('span.nv-comment[data-comment-id]').forEach(span => {
        if (span.getAttribute('data-comment-id') === commentId) clearCommentMark(span);
    });
    window.NovalistEditorState.commentsState.delete(commentId);
    try { window.NovalistEditorState.editor.dispatchEvent(new Event('input', { bubbles: true })); } catch (error) { console.warn("editor: removeCommentSpan failed", error instanceof Error ? error.name : typeof error); }
    window.scheduleCommentRender();
}

function scrollToComment(commentId, doScroll) {
    document.querySelectorAll('span.nv-comment.nv-comment-active').forEach(function (n) {
        n.classList.remove('nv-comment-active');
    });
    if (window.NovalistEditorState.commentGutterEl) {
        Array.from(window.NovalistEditorState.commentGutterEl.children).forEach(function (c) {
            c.classList.toggle('active', c.dataset.commentId === commentId);
        });
    }
    var node = document.querySelector('span.nv-comment[data-comment-id="' + commentId + '"]');
    if (!node) return;
    node.classList.add('nv-comment-active');
    if (doScroll !== false) node.scrollIntoView({ behavior: 'smooth', block: 'center' });
}

// ── Footnotes ──────────────────────────────────────────────────
//
// A footnote's number is not a property of the note: it is where its marker
// stands in the prose. Every message that changes the markers therefore carries
// the whole ordered list, because a note inserted ahead of an existing one
// changes that one's number too. Reporting only the new note's number is what
// put two footnotes on "1".
function renumberFootnotes() {
    var sups = window.NovalistEditorState.editor.querySelectorAll('sup.nv-fn[data-fn-id]');
    var i = 1;
    sups.forEach(function (sup) { sup.textContent = i; i++; });
    return Array.from(sups).map(function (s) { return s.getAttribute('data-fn-id'); });
}

Object.assign(window, {
    wrapSelectionAsComment,
    clearCommentMark,
    removeCommentSpan,
    scrollToComment,
    renumberFootnotes
});
