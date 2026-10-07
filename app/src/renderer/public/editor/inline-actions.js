'use strict';

function inlineActionById(id) {
    for (const action of window.NovalistEditorState.inlineActions || []) {
        if (action.id === id) return action;
    }
    return null;
}

function precedingTextBefore(range) {
    try {
        const scope = document.createRange();
        scope.selectNodeContents(window.NovalistEditorState.editor);
        scope.setEnd(range.startContainer, range.startOffset);
        const text = scope.toString();
        return text.length > window.NovalistEditorState.PRECEDING_TEXT_LIMIT ? text.slice(-window.NovalistEditorState.PRECEDING_TEXT_LIMIT) : text;
    } catch {
        return '';
    }
}

let inlineRequestId = 0;

function triggerInlineAction(id, directive) {
    const action = inlineActionById(id);
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return;

    const collapsed = sel.isCollapsed;
    // An action written before empty selections were a thing still needs one:
    // it would receive an empty SelectedText it was never written to handle.
    if (collapsed && !(action && action.allowsEmptySelection)) return;

    const range = sel.getRangeAt(0).cloneRange();
    const text = collapsed ? '' : sel.toString();
    if (!collapsed && !text) return;

    const requestId = ++inlineRequestId;
    window.NovalistEditorState.pendingInlineAction = { id: id, range: range, requestId, originalText: text };
    window.sendMessage({
        type: 'inlineActionRequested',
        actionId: id,
        requestId,
        selectedText: text,
        precedingText: precedingTextBefore(range),
        directive: directive || ''
    });
}

Object.assign(window, {
    inlineActionById,
    precedingTextBefore,
    triggerInlineAction
});
