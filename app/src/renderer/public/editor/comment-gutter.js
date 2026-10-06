'use strict';

function ensureGutter() {
    if (window.NovalistEditorState.commentGutterEl) return window.NovalistEditorState.commentGutterEl;
    window.NovalistEditorState.commentGutterEl = document.createElement('div');
    window.NovalistEditorState.commentGutterEl.id = 'commentGutter';
    document.body.appendChild(window.NovalistEditorState.commentGutterEl);
    return window.NovalistEditorState.commentGutterEl;
}

function scheduleCommentRender() {
    if (window.NovalistEditorState.commentRenderScheduled) return;
    window.NovalistEditorState.commentRenderScheduled = true;
    requestAnimationFrame(() => {
        window.NovalistEditorState.commentRenderScheduled = false;
        renderCommentGutter();
    });
}

function createCommentCard(id, data) {
    const card = document.createElement('div');
    card.className = 'nv-cc-card';
    card.dataset.commentId = id;

    const anchor = document.createElement('div');
    anchor.className = 'nv-cc-anchor';
    anchor.textContent = data.anchorText || '';
    card.appendChild(anchor);

    const ta = document.createElement('textarea');
    ta.className = 'nv-cc-text';
    ta.value = data.text || '';
    ta.spellcheck = true;
    ta.addEventListener('input', () => {
        const cur = window.NovalistEditorState.commentsState.get(id);
        if (cur) cur.text = ta.value;
        window.sendMessage({ type: 'commentTextChanged', commentId: id, text: ta.value });
        scheduleCommentRender();
    });
    ta.addEventListener('focus', () => {
        window.scrollToComment(id, false);
    });
    card.appendChild(ta);

    const del = document.createElement('button');
    del.className = 'nv-cc-del';
    del.type = 'button';
    del.textContent = '×';
    del.title = 'Delete comment';
    del.addEventListener('click', (e) => {
        e.stopPropagation();
        window.sendMessage({ type: 'commentDeleted', commentId: id });
    });
    card.appendChild(del);

    card.addEventListener('mousedown', (e) => {
        if (e.target === ta || e.target === del) return;
        e.preventDefault();
    });
    card.addEventListener('click', (e) => {
        if (e.target === ta || e.target === del) return;
        window.scrollToComment(id, true);
        ta.focus();
    });
    return card;
}

function renderCommentGutter() {
    const gutter = ensureGutter();
    const liveIds = new Set();
    const items = [];
    const spans = window.NovalistEditorState.editor.querySelectorAll('span.nv-comment[data-comment-id]');
    spans.forEach(s => {
        const id = s.getAttribute('data-comment-id');
        if (!id || !window.NovalistEditorState.commentsState.has(id) || liveIds.has(id)) return;
        liveIds.add(id);
        const r = s.getBoundingClientRect();
        items.push({ id, top: r.top });
    });
    items.sort((a, b) => a.top - b.top);

    const have = new Map();
    Array.from(gutter.children).forEach(c => have.set(c.dataset.commentId, c));
    have.forEach((card, id) => { if (!liveIds.has(id)) card.remove(); });

    let prevBottom = 0;
    const gap = 6;
    items.forEach(item => {
        const data = window.NovalistEditorState.commentsState.get(item.id);
        let card = have.get(item.id);
        if (!card) {
            card = createCommentCard(item.id, data);
            gutter.appendChild(card);
        } else {
            const ta = card.querySelector('textarea');
            if (ta && document.activeElement !== ta && ta.value !== data.text) {
                ta.value = data.text;
            }
            const ae = card.querySelector('.nv-cc-anchor');
            if (ae && ae.textContent !== data.anchorText) ae.textContent = data.anchorText;
        }
        const desired = Math.max(item.top, prevBottom + gap);
        card.style.top = desired + 'px';
        prevBottom = desired + card.offsetHeight;
    });

    document.body.classList.toggle('has-comments', liveIds.size > 0);
}

Object.assign(window, {
    ensureGutter,
    scheduleCommentRender,
    createCommentCard,
    renderCommentGutter
});
