'use strict';

/** Sentence ranges inside one block, in reading order. */
function sentenceRangesIn(block) {
    const ranges = [];
    const walker = document.createTreeWalker(block, NodeFilter.SHOW_TEXT);
    const nodes = [];
    let flat = '';
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
        nodes.push({ node: n, start: flat.length });
        flat += n.nodeValue;
    }
    if (flat.trim().length === 0) return ranges;

    // A sentence runs to its terminator; the tail with no terminator is one too.
    const matcher = /[^.!?…]*[.!?…]+["'”’»]*\s*|[^.!?…]+$/g;
    let m;
    while ((m = matcher.exec(flat)) !== null) {
        const text = m[0];
        if (text.trim().length === 0) continue;
        const range = document.createRange();
        const from = locate(nodes, m.index);
        const to = locate(nodes, m.index + text.replace(/\s+$/, '').length);
        if (!from || !to) continue;
        range.setStart(from.node, from.offset);
        range.setEnd(to.node, to.offset);
        ranges.push({ range: range, text: text.trim() });
    }
    return ranges;
}

/** Maps an offset in a block's flattened text back to (node, offset). */
function locate(nodes, offset) {
    for (let i = nodes.length - 1; i >= 0; i--) {
        if (offset >= nodes[i].start) {
            return { node: nodes[i].node, offset: Math.min(offset - nodes[i].start, nodes[i].node.nodeValue.length) };
        }
    }
    return null;
}

/** The block the caret sits in, so reading can start where the writer is. */
/**
 * The paragraphs, wherever they are sitting.
 *
 * Page view moves every block inside a <div class="nv-page">, so the editor's
 * own children are pages rather than prose. Everything that reasons about "the
 * paragraph" - dimming, paragraph styles, read-aloud - has to look through
 * them, and every one of those was quietly operating on page wrappers instead.
 */
function proseBlocks() {
    const blocks = [];
    for (const child of window.NovalistEditorState.editor.children) {
        if (child.classList && child.classList.contains('nv-page')) {
            for (const inner of child.children) blocks.push(inner);
        } else {
            blocks.push(child);
        }
    }
    return blocks;
}

/** The prose block a node sits in, looking through a page wrapper. */
function blockOf(node) {
    let current = node;
    while (current && current !== window.NovalistEditorState.editor) {
        const parent = current.parentNode;
        if (parent === window.NovalistEditorState.editor) {
            // A page wrapper is not a block. Its first paragraph is the nearest
            // thing, but a caret directly in a wrapper means an empty page.
            if (current.classList && current.classList.contains('nv-page'))
                return current.firstElementChild;
            return current;
        }
        if (parent && parent.classList && parent.classList.contains('nv-page')) return current;
        current = parent;
    }
    return null;
}

function caretBlock() {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return null;
    return blockOf(sel.getRangeAt(0).startContainer);
}

/**
 * Speaks the scene. From the caret's paragraph when asked, otherwise from the
 * top. Voice and rate come from settings; the language comes from the prose,
 * so a German scene is read by a German voice without being told twice.
 */
function startReadAloud(fromCaret, rate, voiceUri) {
    if (typeof speechSynthesis === 'undefined') return;
    stopReadAloud();
    window.NovalistEditorState.readAloudRate = rate > 0 ? rate : 1;
    window.NovalistEditorState.readAloudVoiceUri = voiceUri || null;

    const blocks = proseBlocks();
    const from = fromCaret ? caretBlock() : null;
    const startAt = from ? Math.max(0, blocks.indexOf(from)) : 0;
    window.NovalistEditorState.readAloudQueue = [];
    for (let i = startAt; i < blocks.length; i++) {
        window.NovalistEditorState.readAloudQueue = window.NovalistEditorState.readAloudQueue.concat(sentenceRangesIn(blocks[i]));
    }
    if (window.NovalistEditorState.readAloudQueue.length === 0) return;

    window.NovalistEditorState.readAloudAt = 0;
    window.NovalistEditorState.readAloudActive = true;
    window.sendMessage({ type: 'readAloudStateChanged', speaking: true });
    speakNextSentence();
}

/**
 * True when the chosen voice belongs to the system engine rather than the
 * browser. A SAPI voice id is a token path; a browser one is a URI the browser
 * itself minted, and it is always in getVoices().
 */
function usingSystemVoice() {
    if (!window.NovalistEditorState.readAloudVoiceUri) return false;
    return !speechSynthesis.getVoices().some(function (v) {
        return v.voiceURI === window.NovalistEditorState.readAloudVoiceUri;
    });
}

function speakNextSentence() {
    if (!window.NovalistEditorState.readAloudActive) return;
    if (window.NovalistEditorState.readAloudAt >= window.NovalistEditorState.readAloudQueue.length) { stopReadAloud(); return; }
    const item = window.NovalistEditorState.readAloudQueue[window.NovalistEditorState.readAloudAt++];

    // The system engine, when the writer picked one of its voices. The browser
    // reads one Windows voice store and everything installed to get more
    // voices registers in the other, so this is the only way most of them can
    // be heard at all. A sentence at a time, so the highlight still follows.
    if (usingSystemVoice()) {
        showSpeaking(item.range);
        window.sendMessage({
            type: 'speakSentence',
            text: item.text,
            voiceId: window.NovalistEditorState.readAloudVoiceUri,
            rate: window.NovalistEditorState.readAloudRate
        });
        return;
    }

    const utterance = new SpeechSynthesisUtterance(item.text);
    utterance.lang = window.NovalistEditorState.editor.lang || document.documentElement.lang || 'en';
    utterance.rate = window.NovalistEditorState.readAloudRate;
    if (window.NovalistEditorState.readAloudVoiceUri) {
        const voice = speechSynthesis.getVoices().find(function (v) { return v.voiceURI === window.NovalistEditorState.readAloudVoiceUri; });
        if (voice) utterance.voice = voice;
    }
    utterance.onstart = function () { showSpeaking(item.range); };
    utterance.onend = function () { speakNextSentence(); };
    utterance.onerror = function () { stopReadAloud(); };
    speechSynthesis.speak(utterance);
}

function showSpeaking(range) {
    if (!window.NovalistEditorState.speakingHighlight) return;
    window.NovalistEditorState.speakingHighlight.clear();
    window.NovalistEditorState.speakingHighlight.add(range);

    const rect = range.getBoundingClientRect();
    const box = window.NovalistEditorState.wrapper.getBoundingClientRect();
    // Only when the sentence has actually left the comfortable band. Scrolling
    // on every sentence makes the page twitch its way through a chapter.
    if (rect.bottom > box.bottom - 40 || rect.top < box.top + 40) {
        // Centre the sentence. The old line subtracted half the WRAPPER's
        // height as though it were the sentence's, so reading a line near the
        // top threw the page upward by half a screen - which is what looked
        // like the editor scrolling itself back up.
        window.NovalistEditorState.wrapper.scrollTop += (rect.top - box.top) - (box.height - rect.height) / 2;
    }

    // Dimming follows the voice while it is reading. It marks the caret's
    // paragraph, and the caret does not move when somebody is being read to -
    // so the lit paragraph stayed wherever they last clicked while the voice
    // worked its way down the page.
    if (window.NovalistEditorState.dimOthers) {
        const block = blockOf(range.startContainer);
        if (block) {
            for (const other of proseBlocks()) other.classList.remove('nv-focus-block');
            block.classList.add('nv-focus-block');
        }
    }
}

/** The host has finished speaking one sentence; move to the next. */
function onSentenceSpoken(ok) {
    if (!window.NovalistEditorState.readAloudActive) return;
    if (!ok) { stopReadAloud(); return; }
    speakNextSentence();
}

function stopReadAloud() {
    const wasActive = window.NovalistEditorState.readAloudActive;
    window.NovalistEditorState.readAloudActive = false;
    // Handing the dim back to the caret, which owns it whenever nothing is
    // being read.
    if (window.NovalistEditorState.dimOthers) window.updateFocusBlock();
    window.NovalistEditorState.readAloudQueue = [];
    if (typeof speechSynthesis !== 'undefined') speechSynthesis.cancel();
    // The system engine has its own queue and does not hear speechSynthesis.
    if (wasActive) window.sendMessage({ type: 'stopSystemSpeech' });
    if (window.NovalistEditorState.speakingHighlight) window.NovalistEditorState.speakingHighlight.clear();
    if (wasActive) window.sendMessage({ type: 'readAloudStateChanged', speaking: false });
}

Object.assign(window, {
    sentenceRangesIn,
    locate,
    proseBlocks,
    blockOf,
    caretBlock,
    startReadAloud,
    usingSystemVoice,
    speakNextSentence,
    showSpeaking,
    onSentenceSpoken,
    stopReadAloud
});
