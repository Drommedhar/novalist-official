'use strict';

// ── Grammar Check ───────────────────────────────────────────────

function logToHost(msg) {
    window.sendMessage({ type: 'jsLog', message: msg });
}

function setGrammarCheckEnabled(enabled) {
    logToHost("setGrammarCheckEnabled: enabled=" + enabled);
    window.NovalistEditorState.grammarEnabled = enabled;
    if (!enabled) {
        window.NovalistEditorState.grammarRequestId++;
        window.NovalistEditorState.pendingGrammarRequest = null;
        if (window.NovalistEditorState.grammarCheckTimer) clearTimeout(window.NovalistEditorState.grammarCheckTimer);
        window.NovalistEditorState.grammarCheckTimer = null;
        window.NovalistEditorState.grammarChecking = false;
        window.NovalistEditorState.grammarIssues = [];
        applyGrammarHighlights();
        window.hideGrammarPopup();
    } else {
        // Trigger an initial check if there's content
        window.requestGrammarCheck();
    }
    window.updateGrammarStatusBar();
}

function setGrammarIssues(issuesJson, requestId) {
    if (window.NovalistEditorState.isComposing) return;
    if (requestId !== undefined) {
        if (!window.NovalistEditorState.grammarEnabled || !window.NovalistEditorState.pendingGrammarRequest || requestId !== window.NovalistEditorState.pendingGrammarRequest.id) return;
        const expected = window.NovalistEditorState.pendingGrammarRequest.text;
        window.NovalistEditorState.pendingGrammarRequest = null;
        if (buildGrammarPlainTextMap().plainText !== expected) {
            window.requestGrammarCheck();
            return;
        }
    }
    window.hideGrammarPopup();
    if (window.NovalistEditorState.contextMenu.querySelector('[data-replacement]')) window.hideContextMenu();
    logToHost("setGrammarIssues called: raw issues length = " + (issuesJson ? issuesJson.length : 0));
    try {
        const raw = JSON.parse(issuesJson);
        // Filter out issues that target known entity names. Must use the same
        // plain-text builder that produced the offsets LanguageTool returned,
        // otherwise substring() reads from the wrong position.
        const { plainText } = buildGrammarPlainTextMap();
        window.NovalistEditorState.grammarIssues = raw.filter(issue => {
            const flaggedText = plainText.substring(issue.offset, issue.offset + issue.length);
            return !isKnownEntity(flaggedText);
        });
        logToHost("setGrammarIssues: filtered issues count = " + window.NovalistEditorState.grammarIssues.length);
    } catch (ex) {
        logToHost("setGrammarIssues exception: " + ex);
        window.NovalistEditorState.grammarIssues = [];
    }
    window.NovalistEditorState.grammarChecking = false;
    applyGrammarHighlights();
    window.updateGrammarStatusBar();
}

function isKnownEntity(text) {
    if (!text || window.NovalistEditorState.entityNames.length === 0) return false;
    const lower = text.toLowerCase();
    for (const name of window.NovalistEditorState.entityNames) {
        if (name.toLowerCase() === lower) return true;
    }
    return false;
}

function clearGrammarHighlights() {
    const marks = window.NovalistEditorState.editor.querySelectorAll('.grammar-issue');
    for (const mark of marks) {
        const parent = mark.parentNode;
        while (mark.firstChild) parent.insertBefore(mark.firstChild, mark);
        parent.removeChild(mark);
    }
    // Normalize adjacent text nodes
    window.NovalistEditorState.editor.normalize();
}

function buildGrammarPlainTextMap() {
    // Build the plain text we hand to LanguageTool together with a per-text-node
    // offset map, in lock-step. We must not use editor.innerText for this: it
    // collapses whitespace and trims block boundaries, which drifts the offsets
    // returned by LanguageTool away from the actual text-node positions we need
    // for highlighting and replacement. By concatenating textContent ourselves
    // with explicit '\n' separators between blocks, the offsets LanguageTool
    // returns map exactly onto positions in textNodes[].
    const textNodes = [];
    const parts = [];
    let pos = 0;
    let prevBlockEnd = null;

    function walk(node) {
        if (node.nodeType === Node.TEXT_NODE) {
            const t = node.textContent;
            textNodes.push({ node, start: pos, end: pos + t.length });
            parts.push(t);
            pos += t.length;
        } else if (node.nodeType === Node.ELEMENT_NODE) {
            const tag = node.tagName;
            const isBlock = !node.classList.contains('nv-page') && (tag === 'P' || tag === 'DIV' || tag === 'BR'
                         || tag === 'H1' || tag === 'H2' || tag === 'H3'
                         || tag === 'H4' || tag === 'H5' || tag === 'H6');

            if (isBlock && prevBlockEnd !== null && pos > 0) {
                parts.push('\n');
                pos++;
            }

            if (tag === 'BR') {
                parts.push('\n');
                pos++;
                prevBlockEnd = pos;
            } else {
                const children = node.childNodes;
                for (let i = 0; i < children.length; i++) {
                    walk(children[i]);
                }
                if (isBlock) prevBlockEnd = pos;
            }
        }
    }

    for (let i = 0; i < window.NovalistEditorState.editor.childNodes.length; i++) {
        walk(window.NovalistEditorState.editor.childNodes[i]);
    }
    return { plainText: parts.join(''), textNodes };
}

function isCaretInEditor(sel) {
    // Only scroll for a collapsed caret inside the focused prose surface.
    if (!sel || sel.rangeCount === 0 || !sel.isCollapsed) return false;
    const active = document.activeElement;
    if (active !== window.NovalistEditorState.editor && !window.NovalistEditorState.editor.contains(active)) return false;
    return window.NovalistEditorState.editor.contains(sel.getRangeAt(0).startContainer);
}

function captureProofingSelection() {
    // Setting a selection in contenteditable also focuses it in Chromium.
    // A result arriving while the writer edits a comment must not take focus.
    if (document.activeElement !== window.NovalistEditorState.editor && !window.NovalistEditorState.editor.contains(document.activeElement)) return null;
    const sel = window.getSelection();
    if (!sel || !window.NovalistEditorState.editor.contains(sel.anchorNode) || !window.NovalistEditorState.editor.contains(sel.focusNode)) return null;
    const point = (node, offset) => {
        const block = window.getContainingBlock(node) || window.NovalistEditorState.editor;
        const before = document.createRange();
        before.selectNodeContents(block);
        before.setEnd(node, offset);
        return { block, offset: before.toString().length };
    };
    return { anchor: point(sel.anchorNode, sel.anchorOffset), focus: point(sel.focusNode, sel.focusOffset) };
}

function textPointInElement(element, offset) {
    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
    let node;
    let last = null;
    while ((node = walker.nextNode())) {
        if (offset <= node.length) return { node, offset };
        offset -= node.length;
        last = node;
    }
    return { node: last || element, offset: last ? last.length : 0 };
}

function restoreProofingSelection(saved) {
    if (!saved || !window.NovalistEditorState.editor.contains(saved.anchor.block) || !window.NovalistEditorState.editor.contains(saved.focus.block)) return;
    const anchor = textPointInElement(saved.anchor.block, saved.anchor.offset);
    const focus = textPointInElement(saved.focus.block, saved.focus.offset);
    window.getSelection().setBaseAndExtent(anchor.node, anchor.offset, focus.node, focus.offset);
}

function applyGrammarHighlights() {
    if (window.NovalistEditorState.isComposing) return;
    // Keep both endpoints and selection direction within their own blocks;
    // offsets across the whole editor cannot distinguish empty paragraphs.
    const savedSelection = captureProofingSelection();
    const savedDictation = window.captureDictationPosition();
    const scrollTop = window.NovalistEditorState.wrapper.scrollTop;
    const restoreCaret = () => {
        window.restoreDictationPosition(savedDictation, true);
        restoreProofingSelection(savedSelection);
        window.NovalistEditorState.wrapper.scrollTop = scrollTop;
    };

    // Remove existing highlights
    clearGrammarHighlights();

    // clearGrammarHighlights() normalizes the editor, merging adjacent text
    // nodes, which invalidates the live caret even when there is nothing to
    // highlight — so this path needs the restore too.
    if (!window.NovalistEditorState.grammarIssues || window.NovalistEditorState.grammarIssues.length === 0) {
        restoreCaret();
        // Normalising merged the text nodes the readability ranges sat in.
        window.reapplyReadability();
        return;
    }

    // Use the same plain-text builder that fed LanguageTool so issue offsets
    // align exactly with text-node positions.
    const { textNodes } = buildGrammarPlainTextMap();

    // Sort issues by offset descending so we can apply from the end
    const sorted = [...window.NovalistEditorState.grammarIssues].sort((a, b) => b.offset - a.offset);

    for (const issue of sorted) {
        const issueStart = issue.offset;
        const issueEnd = issue.offset + issue.length;
        const issueIdx = window.NovalistEditorState.grammarIssues.indexOf(issue);

        // Collect every text-node fragment overlapping the issue's range.
        // An issue can straddle multiple text nodes when an inline highlight
        // (entity mention, comment anchor, etc.) splits the underlying word;
        // wrapping only the first fragment would leave the rest of the word
        // outside the .grammar-issue span and survive a later replacement.
        const fragments = [];
        for (let i = 0; i < textNodes.length; i++) {
            const tn = textNodes[i];
            if (tn.end <= issueStart || tn.start >= issueEnd) continue;
            const localStart = Math.max(0, issueStart - tn.start);
            const localEnd = Math.min(tn.node.textContent.length, issueEnd - tn.start);
            if (localStart < localEnd) {
                fragments.push({ node: tn.node, start: localStart, end: localEnd });
            }
        }

        // Wrap each fragment in its own span sharing the same data-issue-index.
        // applyGrammarSuggestion replaces one range spanning all fragments.
        for (const f of fragments) {
            const range = document.createRange();
            range.setStart(f.node, f.start);
            range.setEnd(f.node, f.end);
            const span = document.createElement('span');
            span.className = 'grammar-issue';
            span.setAttribute('data-issue-type', issue.type || 'grammar');
            span.setAttribute('data-issue-index', issueIdx);
            try {
                range.surroundContents(span);
            } catch (error) { console.warn("editor: applyGrammarHighlights failed", error instanceof Error ? error.name : typeof error); }
        }
        const target = window.grammarIssueRange(issueIdx);
        issue.text = target ? target.toString() : '';
    }

    restoreCaret();
    // Wrapping the flagged words split the text nodes the readability ranges
    // were built over, so those ranges now cover nothing.
    window.reapplyReadability();
}

Object.assign(window, {
    logToHost,
    setGrammarCheckEnabled,
    setGrammarIssues,
    isKnownEntity,
    clearGrammarHighlights,
    buildGrammarPlainTextMap,
    isCaretInEditor,
    captureProofingSelection,
    textPointInElement,
    restoreProofingSelection,
    applyGrammarHighlights
});
