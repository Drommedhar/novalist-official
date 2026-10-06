'use strict';

function captureDictationPosition() {
    if (!window.NovalistEditorState.dictationAnchor) return null;
    const range = window.NovalistEditorState.dictationAnchor.range;
    const node = range.startContainer;
    if (!window.NovalistEditorState.editor.contains(node)) return null;
    const block = window.getContainingBlock(node) || window.NovalistEditorState.editor;
    const before = document.createRange();
    before.selectNodeContents(block);
    before.setEnd(node, range.startOffset);
    return { anchor: window.NovalistEditorState.dictationAnchor, node, offset: range.startOffset,
        block, textOffset: before.toString().length };
}

function restoreDictationPosition(saved, textNodesChanged = false) {
    if (!saved || window.NovalistEditorState.dictationAnchor !== saved.anchor) return;
    // Pagination moves intact nodes; proofing splits and merges text nodes.
    // A block-relative text offset also distinguishes empty paragraphs.
    const point = textNodesChanged && window.NovalistEditorState.editor.contains(saved.block)
        ? window.textPointInElement(saved.block, saved.textOffset) : saved;
    if (!window.NovalistEditorState.editor.contains(point.node)) { window.NovalistEditorState.dictationAnchor = null; return; }
    try {
        window.NovalistEditorState.dictationAnchor.range.setStart(point.node, point.offset);
        window.NovalistEditorState.dictationAnchor.range.collapse(true);
    } catch { window.NovalistEditorState.dictationAnchor = null; }
}

Object.assign(window, {
    captureDictationPosition,
    restoreDictationPosition
});
