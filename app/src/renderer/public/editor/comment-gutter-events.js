'use strict';

// ── Margin Comment Gutter ───────────────────────────────────────
const commentsState = new Map();

// id -> { anchorText, text }
let commentGutterEl = null;

let commentRenderScheduled = false;

window.setCommentsData = function (data) {
    try {
        const arr = typeof data === 'string' ? JSON.parse(data) : (data || []);
        commentsState.clear();
        (arr || []).forEach(c => {
            if (!c || !c.id) return;
            commentsState.set(c.id, {
                anchorText: c.anchorText || '',
                text: c.text || ''
            });
        });
        // Older margin deletions saved the shortened annotation list but left
        // its yellow spans in the prose. Repair those orphaned marks as well.
        let changed = false;
        window.NovalistEditorState.editor.querySelectorAll('span.nv-comment[data-comment-id]').forEach(span => {
            if (!commentsState.has(span.getAttribute('data-comment-id'))) {
                window.clearCommentMark(span);
                changed = true;
            }
        });
        if (changed) window.NovalistEditorState.editor.dispatchEvent(new Event('input', { bubbles: true }));
        window.scheduleCommentRender();
    } catch (error) { console.warn("editor: operation failed", error instanceof Error ? error.name : typeof error); }
};

window.NovalistEditorState.wrapper.addEventListener('scroll', window.scheduleCommentRender);

window.addEventListener('resize', window.scheduleCommentRender);

const commentObserver = new MutationObserver(window.scheduleCommentRender);

commentObserver.observe(window.NovalistEditorState.editor, { childList: true, characterData: true, subtree: true });

Object.defineProperties(window.NovalistEditorState, {
    commentGutterEl: { get() { return commentGutterEl; }, set(value) { commentGutterEl = value; } },
    commentRenderScheduled: { get() { return commentRenderScheduled; }, set(value) { commentRenderScheduled = value; } },
    commentsState: { get() { return commentsState; } }
});
