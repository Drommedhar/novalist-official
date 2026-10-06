'use strict';

// ── Dialogue Punctuation Correction ─────────────────────────────

function setDialogueCorrectionConfig(configJson) {
    try {
        window.NovalistEditorState.dialogueCorrectionConfig = JSON.parse(configJson);
        if (!window.NovalistEditorState.dialogueCorrectionConfig || !window.NovalistEditorState.dialogueCorrectionConfig.enabled) {
            window.NovalistEditorState.dialogueCorrectionConfig = null;
        }
    } catch {
        window.NovalistEditorState.dialogueCorrectionConfig = null;
    }
}

function tryDialogueCorrection() {
    if (!window.NovalistEditorState.dialogueCorrectionConfig || window.NovalistEditorState.isComposing) return;

    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0 || !sel.isCollapsed) return;

    const range = sel.getRangeAt(0);
    const node = range.startContainer;

    // Find the paragraph/block element containing the caret
    const paraEl = window.getContainingBlock(node);
    if (!paraEl || paraEl === window.NovalistEditorState.editor) return;

    const text = paraEl.innerText || '';
    if (!text || text.length < 3) return;

    const cfg = window.NovalistEditorState.dialogueCorrectionConfig;
    const oq = cfg.openQuote;
    const cq = cfg.closeQuote;
    const verbs = cfg.speechVerbs;

    const corrected = cfg.ruleFamily === 'de'
        ? window.NovalistDialogue.correctGermanDialogue(text, oq, cq, verbs)
        : window.NovalistDialogue.correctEnglishDialogue(text, oq, cq, verbs);

    if (corrected && corrected !== text) {
        applyParagraphCorrection(paraEl, text, corrected);
    }
}

function applyParagraphCorrection(paraEl, oldText, newText) {
    // 1. Compute caret text offset before correction
    const sel = window.getSelection();
    let caretTextOffset = 0;
    if (sel && sel.rangeCount > 0) {
        const r = sel.getRangeAt(0);
        const preRange = document.createRange();
        preRange.selectNodeContents(paraEl);
        preRange.setEnd(r.startContainer, r.startOffset);
        caretTextOffset = preRange.toString().length;
    }

    // 2. Find diff boundaries
    let diffStart = 0;
    const minLen = Math.min(oldText.length, newText.length);
    while (diffStart < minLen && oldText[diffStart] === newText[diffStart]) diffStart++;

    let oldEnd = oldText.length;
    let newEnd = newText.length;
    while (oldEnd > diffStart && newEnd > diffStart &&
           oldText[oldEnd - 1] === newText[newEnd - 1]) {
        oldEnd--;
        newEnd--;
    }

    // 3. Calculate adjusted caret position
    const delta = (newEnd - diffStart) - (oldEnd - diffStart);
    let newCaretOffset;
    if (caretTextOffset <= diffStart) {
        newCaretOffset = caretTextOffset;
    } else if (caretTextOffset >= oldEnd) {
        newCaretOffset = caretTextOffset + delta;
    } else {
        newCaretOffset = newEnd;
    }

    // 4. Apply change to DOM text nodes
    replaceInTextNodes(paraEl, oldText, diffStart, oldEnd, newText.slice(diffStart, newEnd));

    // 5. Restore caret at adjusted position
    const finalLen = (paraEl.innerText || '').length;
    restoreCaretInElement(paraEl, Math.max(0, Math.min(newCaretOffset, finalLen)));
}

function replaceInTextNodes(element, oldText, diffStart, diffEnd, replacement) {
    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT, null, false);
    const nodes = [];
    let total = 0;
    let nd;
    while ((nd = walker.nextNode())) {
        nodes.push({ node: nd, start: total, end: total + nd.textContent.length });
        total += nd.textContent.length;
    }
    if (total !== oldText.length) return;

    const isInsertion = diffStart === diffEnd;
    let applied = false;
    for (let i = 0; i < nodes.length; i++) {
        const { node: tn, start: ns, end: ne } = nodes[i];

        // Check overlap with changed region
        if (isInsertion) {
            if (diffStart < ns || diffStart > ne) continue;
        } else {
            if (ne <= diffStart || ns >= diffEnd) continue;
        }

        const localStart = Math.max(0, diffStart - ns);
        const localEnd = Math.min(tn.textContent.length, diffEnd - ns);

        if (!applied) {
            tn.textContent = tn.textContent.slice(0, localStart) + replacement + tn.textContent.slice(localEnd);
            applied = true;
        } else {
            tn.textContent = tn.textContent.slice(0, localStart) + tn.textContent.slice(localEnd);
        }
    }
}

function placeCaret(node, offset) {
    const sel = window.getSelection();
    if (!sel) return;
    const range = document.createRange();
    try {
        range.setStart(node, offset);
    } catch {
        return;
    }
    range.collapse(true);
    sel.removeAllRanges();
    sel.addRange(range);
}

function restoreCaretInElement(element, textOffset) {
    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT, null, false);
    let accumulated = 0;
    let node;
    let lastNode = null;
    while ((node = walker.nextNode())) {
        const len = (node.textContent || '').length;
        if (accumulated + len >= textOffset) {
            placeCaret(node, textOffset - accumulated);
            return;
        }
        accumulated += len;
        lastNode = node;
    }
    // No text node reached the offset. An empty block (<p><br></p>) holds no
    // text at all, so collapsing into the element itself is what keeps the caret
    // on that line — returning here would strand it wherever the DOM surgery
    // left it, which is typically the end of the preceding block.
    placeCaret(lastNode || element, lastNode ? (lastNode.textContent || '').length : 0);
}

// ── Caret Position ──────────────────────────────────────────────

function getCaretPosition() {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return { line: 1, column: 1 };

    const range = sel.getRangeAt(0);
    const preRange = range.cloneRange();
    preRange.selectNodeContents(window.NovalistEditorState.editor);
    preRange.setEnd(range.startContainer, range.startOffset);
    const textBefore = preRange.toString();
    const lines = textBefore.split('\n');
    return { line: lines.length, column: lines[lines.length - 1].length + 1 };
}

Object.assign(window, {
    setDialogueCorrectionConfig,
    tryDialogueCorrection,
    applyParagraphCorrection,
    replaceInTextNodes,
    placeCaret,
    restoreCaretInElement,
    getCaretPosition
});
