'use strict';

/** Host pushes localized picker labels. */
function setMentionLabels(labelsJson) {
    try { window.NovalistEditorState.mentionLabels = Object.assign(window.NovalistEditorState.mentionLabels, JSON.parse(labelsJson) || {}); }
    catch (error) { console.warn("editor: setMentionLabels failed", error instanceof Error ? error.name : typeof error); }
}

function setReadabilityEnabled(enabled) {
    window.NovalistEditorState.readabilityEnabled = !!enabled;
    if (!window.NovalistEditorState.readabilityEnabled) {
        window.NovalistEditorState.lastReadabilityGrading = null;
        clearReadability();
        return;
    }
    requestReadability(0);
}

function clearReadability() {
    window.NovalistEditorState.READABILITY_BANDS.forEach(function (band) {
        if (window.NovalistEditorState.readabilityHighlights[band]) window.NovalistEditorState.readabilityHighlights[band].clear();
    });
}

/**
 * Repaints the last grading against the prose as it stands now.
 *
 * The marks are live Ranges, and the DOM under them is rebuilt constantly:
 * page view moves every paragraph into a fresh .nv-page wrapper, and the
 * grammar pass wraps flagged words in spans and normalises what is left.
 * Moving a node out of its parent collapses every Range inside it, so the
 * highlight stayed registered while covering nothing - which is why turning
 * the marking on showed it for an instant and then wiped it.
 *
 * The grading itself survives all of that: none of those rebuilds change a
 * character of the text, so the offsets still point where they did. Repainting
 * from the grading we already have keeps the marks on screen without another
 * round-trip, and without the gap one would leave.
 */
function reapplyReadability() {
    if (!window.NovalistEditorState.readabilityEnabled || !window.NovalistEditorState.lastReadabilityGrading) return;
    paintReadability(window.NovalistEditorState.lastReadabilityGrading);
}

/**
 * Asks the host to grade the text. Debounced, because the grading walks every
 * sentence and the writer is usually still typing the one they are on.
 */
function requestReadability(delay) {
    if (!window.NovalistEditorState.readabilityEnabled || window.NovalistEditorState.isComposing) return;
    if (window.NovalistEditorState.readabilityTimer) clearTimeout(window.NovalistEditorState.readabilityTimer);
    window.NovalistEditorState.readabilityTimer = setTimeout(function () {
        const map = window.buildGrammarPlainTextMap();
        window.sendMessage({ type: 'readabilityRequest', plainText: map.plainText });
    }, delay === 0 ? 0 : 700);
}

/**
 * Paints the graded sentences. The payload carries the band colours too: this
 * document has no access to the shell's design tokens, and hardcoding five
 * colours here would put them outside the token scale.
 */
function setReadability(json) {
    clearReadability();
    window.NovalistEditorState.lastReadabilityGrading = null;
    if (!window.NovalistEditorState.readabilityEnabled) return;
    let payload;
    try { payload = JSON.parse(json); } catch { return; }
    if (!payload || !payload.sentences) return;

    if (payload.colors) {
        const rules = window.NovalistEditorState.READABILITY_BANDS
            .filter(function (band) { return payload.colors[band]; })
            .map(function (band) {
                return '::highlight(nv-read-' + band + ') { background-color: '
                    + payload.colors[band] + '; }';
            })
            .join('\n');
        let sheet = document.getElementById('novalist-readability-colors');
        if (!sheet) {
            sheet = document.createElement('style');
            sheet.id = 'novalist-readability-colors';
            document.head.appendChild(sheet);
        }
        sheet.textContent = rules;
    }

    // Kept so a later rebuild of the prose DOM can be repainted from it rather
    // than leaving the writer with marks that cover nothing.
    window.NovalistEditorState.lastReadabilityGrading = payload.sentences;
    paintReadability(payload.sentences);
}

/** Turns graded sentences into ranges over the prose as it stands now. */
function paintReadability(sentences) {
    clearReadability();
    const map = window.buildGrammarPlainTextMap();
    sentences.forEach(function (sentence) {
        const highlight = window.NovalistEditorState.readabilityHighlights[sentence.level];
        if (!highlight) return;
        const range = rangeForPlainTextSpan(map.textNodes, sentence.offset, sentence.length);
        if (range) highlight.add(range);
    });
}

/** A DOM range covering [offset, offset+length) of the flattened plain text. */
function rangeForPlainTextSpan(textNodes, offset, length) {
    const end = offset + length;
    let from = null;
    let to = null;
    for (let i = 0; i < textNodes.length; i++) {
        const entry = textNodes[i];
        if (!from && offset >= entry.start && offset < entry.end) {
            from = { node: entry.node, offset: offset - entry.start };
        }
        if (end > entry.start && end <= entry.end) {
            to = { node: entry.node, offset: end - entry.start };
        }
    }
    if (!from || !to) return null;
    const range = document.createRange();
    try {
        range.setStart(from.node, from.offset);
        range.setEnd(to.node, to.offset);
    } catch {
        return null;
    }
    return range;
}

Object.assign(window, {
    setMentionLabels,
    setReadabilityEnabled,
    clearReadability,
    reapplyReadability,
    requestReadability,
    setReadability,
    paintReadability,
    rangeForPlainTextSpan
});
