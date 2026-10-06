'use strict';

function setCompletionList(words, trigger) {
    window.NovalistEditorState.completionWords = Array.isArray(words) ? words.filter(function (w) { return !!w; }) : [];
    window.NovalistEditorState.completionTrigger = trigger > 0 ? trigger : 3;
    closeCompletion();
}

/** The word being typed at the caret, or null when there is not one. */
function wordAtCaret() {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0 || !sel.isCollapsed) return null;
    const range = sel.getRangeAt(0);
    if (range.startContainer.nodeType !== Node.TEXT_NODE) return null;
    const text = range.startContainer.textContent || '';
    const offset = range.startOffset;

    let start = offset;
    while (start > 0 && /[\p{L}\p{M}'\u2019-]/u.test(text.charAt(start - 1))) start--;
    if (offset - start < window.NovalistEditorState.completionTrigger) return null;
    return { node: range.startContainer, start: start, query: text.slice(start, offset) };
}

function updateCompletion() {
    if (window.NovalistEditorState.completionWords.length === 0) { closeCompletion(); return; }
    const found = wordAtCaret();
    if (!found) { closeCompletion(); return; }

    const typed = found.query;
    const lower = typed.toLowerCase();
    window.NovalistEditorState.completionMatches = [];
    for (let i = 0; i < window.NovalistEditorState.completionWords.length && window.NovalistEditorState.completionMatches.length < 8; i++) {
        const word = window.NovalistEditorState.completionWords[i];
        if (word.length === typed.length) continue;      // completes nothing
        if (word.toLowerCase().indexOf(lower) !== 0) continue;
        window.NovalistEditorState.completionMatches.push(word);
    }
    if (window.NovalistEditorState.completionMatches.length === 0) { closeCompletion(); return; }

    window.NovalistEditorState.completionState = found;
    window.NovalistEditorState.completionIndex = 0;
    renderCompletion();
}

function closeCompletion() {
    window.NovalistEditorState.completionState = null;
    window.NovalistEditorState.completionMatches = [];
    const picker = document.getElementById('completion-picker');
    if (picker) picker.classList.remove('visible');
}

function renderCompletion() {
    let picker = document.getElementById('completion-picker');
    if (!picker) {
        picker = document.createElement('div');
        picker.id = 'completion-picker';
        picker.className = 'mention-picker';
        document.body.appendChild(picker);
    }
    picker.innerHTML = '';
    window.NovalistEditorState.completionMatches.forEach(function (word, i) {
        const row = document.createElement('div');
        row.className = 'mention-item' + (i === window.NovalistEditorState.completionIndex ? ' selected' : '');
        row.textContent = word;
        row.addEventListener('mousedown', function (e) {
            e.preventDefault();
            acceptCompletion(i);
        });
        picker.appendChild(row);
    });

    const sel = window.getSelection();
    if (sel && sel.rangeCount > 0) {
        const rect = sel.getRangeAt(0).getBoundingClientRect();
        picker.style.left = rect.left + 'px';
        picker.style.top = (rect.bottom + 4) + 'px';
    }
    picker.classList.add('visible');
}

function acceptCompletion(index) {
    if (!window.NovalistEditorState.completionState) return;
    const word = window.NovalistEditorState.completionMatches[index];
    if (!word) return;

    const node = window.NovalistEditorState.completionState.node;
    const text = node.textContent || '';
    const sel = window.getSelection();
    const end = sel && sel.rangeCount > 0 ? sel.getRangeAt(0).startOffset : window.NovalistEditorState.completionState.start;
    node.textContent = text.slice(0, window.NovalistEditorState.completionState.start) + word + text.slice(end);

    const range = document.createRange();
    range.setStart(node, window.NovalistEditorState.completionState.start + word.length);
    range.collapse(true);
    sel.removeAllRanges();
    sel.addRange(range);

    closeCompletion();
    // Accepting a completion is an edit, and the editor only notices edits it
    // sees as input events - this one replaces text directly.
    window.NovalistEditorState.editor.dispatchEvent(new Event('input', { bubbles: true }));
}

Object.assign(window, {
    setCompletionList,
    wordAtCaret,
    updateCompletion,
    closeCompletion,
    renderCompletion,
    acceptCompletion
});
