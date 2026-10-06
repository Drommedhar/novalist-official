'use strict';

function queueContentChangedSoon() {
    // Lightweight nudge — dispatch input event so the input listener picks it up.
    window.NovalistEditorState.editor.dispatchEvent(new Event('input', { bubbles: true }));
}

/** A DOM range over [start,end) of an element's text, for measuring a hit. */
function rangeForTextOffsets(parentEl, start, end) {
    const walker = document.createTreeWalker(parentEl, NodeFilter.SHOW_TEXT, null, false);
    const range = document.createRange();
    let node, pos = 0, started = false;
    while ((node = walker.nextNode())) {
        const len = (node.textContent || '').length;
        if (!started && pos + len > start) {
            range.setStart(node, Math.max(0, start - pos));
            started = true;
        }
        if (started && pos + len >= end) {
            range.setEnd(node, Math.max(0, end - pos));
            return range;
        }
        pos += len;
    }
    return null;
}

function findEntityAtPoint(x, y) {
    if (!window.NovalistEditorState.entityRegex) return null;

    let range;
    if (document.caretRangeFromPoint) {
        range = document.caretRangeFromPoint(x, y);
    } else if (document.caretPositionFromPoint) {
        const pos = document.caretPositionFromPoint(x, y);
        if (pos) {
            range = document.createRange();
            range.setStart(pos.offsetNode, pos.offset);
            range.collapse(true);
        }
    }
    if (!range || !range.startContainer || range.startContainer.nodeType !== Node.TEXT_NODE) return null;

    const textNode = range.startContainer;
    const offset = range.startOffset;

    // Find the word boundaries around the offset, then check broader context
    // We check the full line (parent element text) for entity matches
    const parentEl = textNode.parentElement;
    if (!parentEl) return null;
    const lineText = parentEl.innerText || '';
    if (!lineText) return null;

    // Calculate offset within the parent element text
    let charOffset = 0;
    const walker = document.createTreeWalker(parentEl, NodeFilter.SHOW_TEXT, null, false);
    let node;
    while ((node = walker.nextNode())) {
        if (node === textNode) {
            charOffset += offset;
            break;
        }
        charOffset += (node.textContent || '').length;
    }

    // Find entity alias at this position
    const globalRegex = new RegExp(window.NovalistEditorState.entityRegex.source, 'giu');
    let match;
    while ((match = globalRegex.exec(lineText)) !== null) {
        if (charOffset >= match.index && charOffset <= match.index + match[0].length) {
            if (!window.entityHitAllowed(match[0], lineText)) return null;
            // The rect of the word itself, not the pointer. The host places the
            // peek clear of the whole name, so drifting a few pixels inside it
            // never moves the pointer onto the card.
            const hitRange = rangeForTextOffsets(parentEl, match.index, match.index + match[0].length);
            const box = hitRange ? hitRange.getBoundingClientRect() : null;
            return {
                alias: match[0],
                rect: box && box.width > 0
                    ? { left: box.left, top: box.top, right: box.right, bottom: box.bottom }
                    : null
            };
        }
    }
    return null;
}

function findEntityAtCaret() {
    if (!window.NovalistEditorState.entityRegex) return null;

    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0 || !sel.isCollapsed) return null;

    const range = sel.getRangeAt(0);
    if (!range.startContainer || range.startContainer.nodeType !== Node.TEXT_NODE) return null;

    const textNode = range.startContainer;
    const offset = range.startOffset;
    const parentEl = textNode.parentElement;
    if (!parentEl) return null;
    const lineText = parentEl.innerText || '';
    if (!lineText) return null;

    let charOffset = 0;
    const walker = document.createTreeWalker(parentEl, NodeFilter.SHOW_TEXT, null, false);
    let node;
    while ((node = walker.nextNode())) {
        if (node === textNode) {
            charOffset += offset;
            break;
        }
        charOffset += (node.textContent || '').length;
    }

    const globalRegex = new RegExp(window.NovalistEditorState.entityRegex.source, 'giu');
    let match;
    while ((match = globalRegex.exec(lineText)) !== null) {
        if (charOffset >= match.index && charOffset <= match.index + match[0].length) {
            return window.entityHitAllowed(match[0], lineText) ? match[0] : null;
        }
    }
    return null;
}

function getCaretRect() {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return null;
    const range = sel.getRangeAt(0).cloneRange();
    range.collapse(true);
    const rect = range.getBoundingClientRect();
    if (!rect || (rect.x === 0 && rect.y === 0 && rect.width === 0 && rect.height === 0)) return null;
    return rect;
}

/** Keyboard-accessible counterpart to hover/tap focus peek. */
function peekEntityAtCaret() {
    const alias = findEntityAtCaret();
    const rect = getCaretRect();
    if (!alias || !rect) return false;
    window.sendMessage({
        type: 'entityHover',
        alias: alias,
        x: rect.left + Math.max(1, rect.width / 2),
        y: rect.bottom,
        // The card is anchored to what it belongs to rather than to a point,
        // the same as the hover path - so a peek raised from the keyboard or
        // the menu sits clear of the caret instead of on top of it.
        rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom }
    });
    return true;
}

Object.assign(window, {
    queueContentChangedSoon,
    rangeForTextOffsets,
    findEntityAtPoint,
    findEntityAtCaret,
    getCaretRect,
    peekEntityAtCaret
});
