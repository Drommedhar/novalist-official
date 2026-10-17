'use strict';

let pendingQuote = null;

function setAutoReplacements(pairsJson) {
    pendingQuote = null;
    let parsed;
    try {
        parsed = JSON.parse(pairsJson);
    } catch {
        parsed = [];
    }
    window.NovalistEditorState.autoReplacements = [];
    for (const pair of (parsed || [])) {
        if ((pair.kind || 'literal') !== 'regex') {
            window.NovalistEditorState.autoReplacements.push(pair);
            continue;
        }
        // Anchored at the caret: a rule fires on what has just been finished,
        // not on something further back that the writer already moved past.
        try {
            const regex = new RegExp('(?:' + pair.start + ')$');
            // A pattern that matches the empty string matches before every
            // keystroke, forever. The backend refuses these; this is the same
            // guard for a settings file edited by hand.
            if (regex.test('')) continue;
            window.NovalistEditorState.autoReplacements.push(Object.assign({}, pair, { regex: regex, tooSlow: false }));
        } catch (error) { console.warn("editor: setAutoReplacements failed", error instanceof Error ? error.name : typeof error); }
    }
}

/** Puts the captured groups into a replacement: $1..$9, and $$ for a literal $. */
function expandCaptures(template, match) {
    return template.replace(/\$(\$|\d)/g, function (whole, token) {
        if (token === '$') return '$';
        const group = match[Number(token)];
        return group === undefined ? '' : group;
    });
}

function getContainingBlock(node) {
    if (!node) return null;
    let el = node.nodeType === Node.TEXT_NODE ? node.parentElement : node;
    const blockTags = ['P', 'DIV', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'BLOCKQUOTE', 'LI'];
    while (el && el !== window.NovalistEditorState.editor) {
        if (blockTags.includes(el.tagName)) {
            return el;
        }
        if (el.parentElement === window.NovalistEditorState.editor) {
            return el;
        }
        el = el.parentElement;
    }
    return null;
}

function getReplacementContext() {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return null;
    const range = sel.getRangeAt(0);
    const editor = window.NovalistEditorState.editor;
    if (!range.collapsed || !editor.contains(range.startContainer)) return null;
    const paraEl = getContainingBlock(range.startContainer) || editor;
    const preRange = document.createRange();
    preRange.selectNodeContents(paraEl);
    preRange.setEnd(range.startContainer, range.startOffset);
    const postRange = document.createRange();
    postRange.selectNodeContents(paraEl);
    postRange.setStart(range.startContainer, range.startOffset);
    return { paraEl, textBeforePara: preRange.toString(), textAfterPara: postRange.toString() };
}

function tryAutoReplace(inputText) {
    if (window.NovalistEditorState.autoReplacements.length === 0 || !inputText) return null;
    const context = getReplacementContext();
    if (!context) {
        pendingQuote = null;
        return null;
    }
    const { paraEl, textBeforePara, textAfterPara } = context;
    const lineText = textBeforePara + inputText;
    const continued = pendingQuote?.block === paraEl && !textAfterPara
        ? window.NovalistQuoteContext.continueWord(pendingQuote, textBeforePara, inputText)
        : null;
    pendingQuote = null;
    if (continued) return continued;

    for (const pair of window.NovalistEditorState.autoReplacements) {
        if (pair.regex) {
            if (pair.tooSlow) continue;
            // Only the tail is offered: the cost of a bad pattern grows with
            // what it is given, and no rule needs the whole paragraph.
            const tail = lineText.length > window.NovalistEditorState.REGEX_LOOKBEHIND
                ? lineText.slice(-window.NovalistEditorState.REGEX_LOOKBEHIND)
                : lineText;
            const started = performance.now();
            const match = pair.regex.exec(tail);
            if (performance.now() - started > window.NovalistEditorState.REGEX_BUDGET_MS) {
                pair.tooSlow = true;
                console.warn('[AutoReplace] rule set aside for this session, too slow:', pair.start);
                continue;
            }
            // The match has to cover what was just typed, or the replacement
            // would eat characters the writer did not type in this keystroke.
            if (match && match[0].length >= inputText.length) {
                return {
                    replacement: expandCaptures(pair.startReplace, match),
                    backtrack: match[0].length - inputText.length
                };
            }
            continue;
        }

        const quote = window.NovalistQuoteContext.replace(pair, textBeforePara, inputText, textAfterPara);
        if (quote) {
            if (quote.provisional) {
                pendingQuote = {
                    block: paraEl,
                    before: textBeforePara + quote.replacement,
                    length: quote.replacement.length
                };
            }
            return quote;
        }

        if (lineText.endsWith(pair.start)) {
            return {
                replacement: pair.startReplace,
                backtrack: pair.start.length - inputText.length
            };
        }
    }
    return null;
}

Object.assign(window, {
    setAutoReplacements,
    expandCaptures,
    getContainingBlock,
    tryAutoReplace
});

for (const event of ['pointerdown', 'blur', 'compositionstart']) {
    document.getElementById('editor').addEventListener(event, () => { pendingQuote = null; });
}
document.getElementById('editor').addEventListener('keydown', (event) => {
    if (event.ctrlKey || event.metaKey || /^(Arrow|Home|End|Page|Backspace|Delete|Enter|Tab|Escape)/.test(event.key)) {
        pendingQuote = null;
    }
});
