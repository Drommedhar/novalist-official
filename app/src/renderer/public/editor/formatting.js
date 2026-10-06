'use strict';

// ── Formatting Commands ─────────────────────────────────────────

function toggleBold() {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    document.execCommand('bold', false, null);
    notifyFormattingChanged();
}

function toggleItalic() {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    document.execCommand('italic', false, null);
    notifyFormattingChanged();
}

function toggleUnderline() {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    document.execCommand('underline', false, null);
    notifyFormattingChanged();
}

function toggleStrikethrough() {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    document.execCommand('strikeThrough', false, null);
    notifyFormattingChanged();
}

/**
 * Marks a passage to come back to.
 *
 * A class rather than an inline colour, so a theme can restyle it and an
 * export can decide whether a highlight means anything in that format.
 */
function toggleHighlight() {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0 || sel.isCollapsed) return;

    const existing = highlightAncestor(sel.anchorNode);
    if (existing) {
        // Unwrap: the writer is done with that passage.
        const parent = existing.parentNode;
        while (existing.firstChild) parent.insertBefore(existing.firstChild, existing);
        parent.removeChild(existing);
        notifyFormattingChanged();
        return;
    }

    const range = sel.getRangeAt(0);
    const mark = document.createElement('span');
    mark.className = 'nv-highlight';
    try {
        mark.appendChild(range.extractContents());
        range.insertNode(mark);
    } catch {
        // A selection spanning block boundaries cannot be wrapped in one span;
        // leaving the prose untouched beats producing broken markup.
        return;
    }
    notifyFormattingChanged();
}

function highlightAncestor(node) {
    while (node && node !== window.NovalistEditorState.editor) {
        if (node.nodeType === 1 && node.classList && node.classList.contains('nv-highlight'))
            return node;
        node = node.parentNode;
    }
    return null;
}

/** Turns the selection into a link, or removes the link it already is. */
function applyLink(href) {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    if (!href) {
        document.execCommand('unlink', false, null);
    } else {
        document.execCommand('createLink', false, href);
    }
    notifyFormattingChanged();
}

function alignLeft() {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    document.execCommand('justifyLeft', false, null);
    notifyFormattingChanged();
}

function alignCenter() {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    document.execCommand('justifyCenter', false, null);
    notifyFormattingChanged();
}

function alignRight() {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    document.execCommand('justifyRight', false, null);
    notifyFormattingChanged();
}

function alignJustify() {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    document.execCommand('justifyFull', false, null);
    notifyFormattingChanged();
}

/** Block elements (top-level children of the editor) touched by the selection. */
function selectedBlocks() {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) return [];
    const range = selection.getRangeAt(0);
    const blocks = [];
    for (const block of window.proseBlocks()) {
        if (range.intersectsNode(block)) blocks.push(block);
    }
    // A collapsed caret inside a block still has to resolve to that block.
    if (blocks.length === 0) {
        const block = window.blockOf(range.startContainer);
        if (block) blocks.push(block);
    }
    return blocks;
}

/** The style id shared by the selected blocks, or '' for plain body text. */
function getParagraphStyle() {
    const blocks = selectedBlocks();
    if (blocks.length === 0) return '';
    const styleOf = (block) => {
        for (const cls of block.classList || []) {
            if (cls.startsWith(window.NovalistEditorState.PARAGRAPH_STYLE_PREFIX)) return cls.slice(window.NovalistEditorState.PARAGRAPH_STYLE_PREFIX.length);
        }
        return '';
    };
    const first = styleOf(blocks[0]);
    return blocks.every((b) => styleOf(b) === first) ? first : '';
}

/** Applies a named paragraph style to the selection; '' clears it. */
function setParagraphStyle(style) {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    const blocks = selectedBlocks();
    if (blocks.length === 0) return;
    for (const block of blocks) {
        if (!block.classList) continue;
        const previousStyles = Array.from(block.classList).filter(cls => cls.startsWith(window.NovalistEditorState.PARAGRAPH_STYLE_PREFIX));
        block.classList.remove(...previousStyles);
        if (style) block.classList.add(window.NovalistEditorState.PARAGRAPH_STYLE_PREFIX + style);
        if (block.classList.length === 0) block.removeAttribute('class');
    }
    notifyFormattingChanged();
    // A class change is not typing, so no input event fires on its own — but
    // the host still has to hear about it or the style would never be saved.
    window.NovalistEditorState.editor.dispatchEvent(new Event('input'));
}

// Lists are real ul/ol structure rather than a paragraph style, because that is
// what every export format wants and what a pasted list already is.
function toggleBulletList() {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    document.execCommand('insertUnorderedList');
    notifyFormattingChanged();
    window.NovalistEditorState.editor.dispatchEvent(new Event('input'));
}

function toggleNumberList() {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    document.execCommand('insertOrderedList');
    notifyFormattingChanged();
    window.NovalistEditorState.editor.dispatchEvent(new Event('input'));
}

// ── Formatting State ────────────────────────────────────────────

function getFormattingState() {
    const selection = window.getSelection();
    const hasSelection = !!selection && !selection.isCollapsed && selection.toString().trim() !== '';
    let linkActive = false;
    let ancestor = selection && selection.anchorNode;
    while (ancestor && ancestor !== window.NovalistEditorState.editor) {
        if (ancestor.nodeType === Node.ELEMENT_NODE && ancestor.tagName === 'A') {
            linkActive = true;
            break;
        }
        ancestor = ancestor.parentNode;
    }
    const bold = document.queryCommandState('bold');
    const italic = document.queryCommandState('italic');
    const underline = document.queryCommandState('underline');
    let alignment = 'left';
    if (document.queryCommandState('justifyCenter')) alignment = 'center';
    else if (document.queryCommandState('justifyRight')) alignment = 'right';
    else if (document.queryCommandState('justifyFull')) alignment = 'justify';
    return {
        bold,
        italic,
        underline,
        hasSelection,
        linkActive,
        entityAtCaret: !hasSelection && !!window.findEntityAtCaret(),
        strikethrough: document.queryCommandState('strikeThrough'),
        highlight: !!highlightAncestor(window.getSelection() && window.getSelection().anchorNode),
        alignment,
        paragraphStyle: getParagraphStyle(),
        bulletList: document.queryCommandState('insertUnorderedList'),
        numberList: document.queryCommandState('insertOrderedList')
    };
}

function notifyFormattingChanged() {
    const state = getFormattingState();
    window.sendMessage({ type: 'formattingChanged', ...state });
}

Object.assign(window, {
    toggleBold,
    toggleItalic,
    toggleUnderline,
    toggleStrikethrough,
    toggleHighlight,
    highlightAncestor,
    applyLink,
    alignLeft,
    alignCenter,
    alignRight,
    alignJustify,
    selectedBlocks,
    getParagraphStyle,
    setParagraphStyle,
    toggleBulletList,
    toggleNumberList,
    getFormattingState,
    notifyFormattingChanged
});
