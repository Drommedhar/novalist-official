'use strict';

// A live Range follows ordinary edits, but is invalidated when the host loads
// another scene. Layout and proofing must preserve it separately from the
// visible selection: moving a paragraph collapses its live Ranges to the parent.
let dictationAnchor = null;

window.captureDictationAnchor = function (id) {
    const sel = window.getSelection();
    let range = sel && sel.rangeCount ? sel.getRangeAt(0).cloneRange() : null;
    if (!range || !window.NovalistEditorState.editor.contains(range.startContainer)) {
        range = document.createRange();
        range.selectNodeContents(window.NovalistEditorState.editor);
    }
    range.collapse(false);
    dictationAnchor = { id, range };
    return true;
};

window.focusDictationCaret = function () {
    // Focus preserves the editor's DOM selection while controls outside the
    // iframe are used. Collapse a selection after it, as local dictation does.
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    const sel = window.getSelection();
    if (!sel || !sel.rangeCount || !window.NovalistEditorState.editor.contains(sel.getRangeAt(0).startContainer)) return false;
    sel.collapseToEnd();
    return true;
};

window.insertDictationText = function (id, text, paragraph, mergeClose, mergeOpen, tag) {
    if (!dictationAnchor || dictationAnchor.id !== id || window.NovalistEditorState.isComposing) return false;
    const range = dictationAnchor.range;
    if (!window.NovalistEditorState.editor.contains(range.startContainer)) return false;
    const sel = window.getSelection();
    const saved = sel && sel.rangeCount ? sel.getRangeAt(0).cloneRange() : null;
    const following = saved && saved.collapsed && saved.startContainer === range.startContainer
        && saved.startOffset === range.startOffset;
    const active = document.activeElement;
    const node = range.startContainer;
    let attachPunctuation = false;
    // A pause can separate dialogue from its speech tag. Repair only the
    // generated closing punctuation, without asking the model to rewrite it.
    if (tag && node.nodeType === 3) {
        const preceding = node.textContent.slice(0, range.startOffset);
        if (preceding.endsWith(tag.close)) {
            const speech = preceding.slice(0, -tag.close.length);
            const removePeriod = speech.endsWith('.');
            const removeComma = tag.language === 'de' && speech.endsWith(',');
            const count = tag.close.length + (removePeriod || removeComma ? 1 : 0);
            range.setStart(node, range.startOffset - count);
            text = (tag.language === 'de' ? tag.close + ',' : (removePeriod ? ',' : '') + tag.close) + ' ' + text;
            attachPunctuation = true;
        }
    }
    // Only remove a quote we just generated, still immediately at the anchor.
    if (mergeClose) {
        if (node.nodeType === 3 && node.textContent.slice(0, range.startOffset).endsWith(mergeClose)) {
            range.setStart(node, range.startOffset - mergeClose.length);
        } else {
            text = mergeOpen + text;
        }
    }
    const element = node.nodeType === 3 ? node.parentElement : node;
    const block = element.closest('p,div,li,h1,h2,h3,blockquote') || window.NovalistEditorState.editor;
    const before = document.createRange();
    before.selectNodeContents(block);
    before.setEnd(range.startContainer, range.startOffset);
    const prefix = before.toString();
    if (prefix.trim()) {
        text = (paragraph ? '\n' : attachPunctuation || /\s$/.test(prefix) || /^[,.;:!?]/.test(text) ? '' : ' ') + text;
    }
    window.NovalistEditorState.editor.focus();
    sel.removeAllRanges();
    sel.addRange(range);
    if (window.NovalistEditorState.suggestionMode) window.insertSuggested(text);
    else if (!document.execCommand('insertText', false, text)) return false;
    dictationAnchor.range = sel.getRangeAt(0).cloneRange();
    dictationAnchor.range.collapse(false);
    if (saved && !following && window.NovalistEditorState.editor.contains(saved.startContainer)) {
        sel.removeAllRanges();
        sel.addRange(saved);
    }
    if (active && active !== window.NovalistEditorState.editor && active.focus) active.focus();
    window.NovalistEditorState.editor.dispatchEvent(new Event('input', { bubbles: true }));
    return true;
};

window.applyInlineActionResult = function (json) {
    try {
        var payload = typeof json === 'string' ? JSON.parse(json) : json;
        if (!payload || !window.NovalistEditorState.pendingInlineAction) return;
        if (payload.actionId !== window.NovalistEditorState.pendingInlineAction.id) { window.NovalistEditorState.pendingInlineAction = null; return; }
        if (payload.error) { window.NovalistEditorState.pendingInlineAction = null; return; }

        const range = window.NovalistEditorState.pendingInlineAction.range;
        const disposition = payload.disposition || 'replace';
        const text = payload.text || '';

        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(range);

        if (disposition === 'insertAtCaret') {
            // Replaces nothing. With a selection this collapses to its end,
            // which is what "carry on from here" means either way.
            range.collapse(false);
            document.execCommand('insertText', false, text);
        } else if (disposition === 'insertAfter') {
            range.collapse(false);
            document.execCommand('insertText', false, '\n' + text);
        } else {
            document.execCommand('insertText', false, text);
        }

        window.NovalistEditorState.pendingInlineAction = null;
        try { window.NovalistEditorState.editor.dispatchEvent(new Event('input', { bubbles: true })); } catch (error) { console.warn("editor: operation failed", error instanceof Error ? error.name : typeof error); }
    } catch {
        window.NovalistEditorState.pendingInlineAction = null;
    }
};

Object.defineProperties(window.NovalistEditorState, {
    dictationAnchor: { get() { return dictationAnchor; }, set(value) { dictationAnchor = value; } }
});
