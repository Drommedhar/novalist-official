'use strict';

function setAutoReplacements(pairsJson) {
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

function tryAutoReplace(inputText) {
    if (window.NovalistEditorState.autoReplacements.length === 0 || !inputText) return null;

    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return null;
    const range = sel.getRangeAt(0);
    if (!range.collapsed) return null;

    const textNode = range.startContainer;

    // Find the block element containing the caret
    const paraEl = getContainingBlock(textNode) || window.NovalistEditorState.editor;

    // Get the text of the paragraph before the caret
    let textBeforePara = '';
    try {
        const preRange = document.createRange();
        preRange.selectNodeContents(paraEl);
        preRange.setEnd(range.startContainer, range.startOffset);
        textBeforePara = preRange.toString();
    } catch {
        // Fallback to text node if range calculation fails
        if (textNode.nodeType === Node.TEXT_NODE) {
            const text = textNode.textContent || '';
            const offset = range.startOffset;
            textBeforePara = text.slice(0, offset);
        } else {
            textBeforePara = '';
        }
    }

    // Text as it will appear after insertion up to the caret
    const lineText = textBeforePara + inputText;

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

        // Paired quotes: same trigger, different open/close
        if (pair.start === pair.end && pair.startReplace !== pair.endReplace) {
            if (lineText.endsWith(pair.end)) {
                // Count quotes excluding those surrounded by alphabet characters on both sides
                // (e.g., apostrophes in "it's", "don't")
                const opens = countOccurrences(lineText, pair.startReplace, true);
                const closes = countOccurrences(lineText, pair.endReplace, true);
                if (opens > closes) {
                    return {
                        replacement: pair.endReplace,
                        backtrack: pair.end.length - inputText.length
                    };
                }
            }
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

function countOccurrences(text, token, skipIfSurroundedByAlpha = false) {
    if (!token) return 0;
    let count = 0, idx = 0;
    while ((idx = text.indexOf(token, idx)) >= 0) {
        const before = text[idx - 1];
        const after = text[idx + token.length];
        const surroundedByAlpha = skipIfSurroundedByAlpha && before && after && /[a-z]/i.test(before) && /[a-z]/i.test(after);
        if (!surroundedByAlpha) count++;
        idx += token.length;
    }
    return count;
}

Object.assign(window, {
    setAutoReplacements,
    expandCaptures,
    getContainingBlock,
    tryAutoReplace,
    countOccurrences
});
