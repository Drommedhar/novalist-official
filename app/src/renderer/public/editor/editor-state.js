'use strict';

const editor = document.getElementById('editor');

const wrapper = document.getElementById('editor-wrapper');

// ── State ────────────────────────────────────────────────────────
let isSettingContent = false;

let isComposing = false;

let compositionEndTimer = null;

let lastHtml = '';

let entityRegex = null;

let entityNames = [];

let entityIndex = new Map();

// lowercase name → {entityId, entityType, isAlias}
let entityMatchRules = new Map();

// lowercase name → {caseSensitive, exact, exclusions[]}
let autoReplacements = [];

let lastHoveredAlias = null;

let lastHoveredMentionId = null;

let entityExitTimer = null;

let dialogueCorrectionConfig = null;

let dialogueCorrectionTimer = null;

// Mention picker state
let mentionState = null;

// {startNode, startOffset, queryNode, query} when picker is active
let mentionCandidates = [];

// full list pushed by host
let mentionFiltered = [];

let mentionSelectedIndex = 0;

let mentionCreateVisible = false;

// "Create <name>" row shown as the last entry
let mentionPendingSeq = 0;

// ids for spans awaiting a host-created entity
let mentionLabels = { create: 'Create "{name}"', noMatches: 'No matches' };

// Focus scroll prevention state
let savedScrollTop = 0;

let isFocusing = false;

// Typewriter scroll state
let typewriterEnabled = false;

let typewriterAnchor = 'middle';

// 'top' | 'middle' | 'bottom'
let typewriterFrame = 0;

let typewriterLastY = -9999;

let typewriterMouseDown = false;

let typewriterSuspendUntil = 0;

// ms epoch — suppress recenter until then
// ms epoch — a scroll the editor issued itself (caret recentring), which must
// not be mistaken for the user scrolling away from an open context menu.
let programmaticScrollUntil = 0;

// ── C#→JS Bridge ────────────────────────────────────────────────

// ── Readability marking ─────────────────────────────────────────
// A sentence at a time, tinted by how hard it is to read. Painted with the
// Custom Highlight API for the same reason read-aloud is: a report about the
// prose must never edit the prose.

let readabilityEnabled = false;

let readabilityTimer = null;

/** The sentences the host last graded, so a DOM rebuild can be repainted. */
let lastReadabilityGrading = null;

const READABILITY_BANDS = ['VeryEasy', 'Easy', 'Moderate', 'Difficult', 'VeryDifficult'];

const readabilityHighlights = {};

if (typeof Highlight === 'function' && typeof CSS !== 'undefined' && CSS.highlights) {
    READABILITY_BANDS.forEach(function (band) {
        readabilityHighlights[band] = new Highlight();
        CSS.highlights.set('nv-read-' + band, readabilityHighlights[band]);
    });
}

// ── Read aloud ──────────────────────────────────────────────────
// Sentence by sentence, so the highlight has something to sit on and so
// stopping lands between sentences rather than mid-word.

let readAloudActive = false;

let readAloudQueue = [];

let readAloudAt = 0;

let readAloudRate = 1;

let readAloudVoiceUri = null;

const speakingHighlight = typeof Highlight === 'function' ? new Highlight() : null;

if (speakingHighlight && typeof CSS !== 'undefined' && CSS.highlights) {
    CSS.highlights.set('nv-speaking', speakingHighlight);
}

// ── Paragraph Styles ────────────────────────────────────────────
// Named paragraph styles live as an `nv-style-<id>` class on the block. The
// exporters read that class to turn a paragraph into a heading; the editor
// paints it so the writer can see which paragraphs carry one.

const PARAGRAPH_STYLE_PREFIX = 'nv-style-';

Object.defineProperties(window.NovalistEditorState, {
    mentionLabels: { get() { return mentionLabels; }, set(value) { mentionLabels = value; } },
    readabilityEnabled: { get() { return readabilityEnabled; }, set(value) { readabilityEnabled = value; } },
    lastReadabilityGrading: { get() { return lastReadabilityGrading; }, set(value) { lastReadabilityGrading = value; } },
    READABILITY_BANDS: { get() { return READABILITY_BANDS; } },
    readabilityHighlights: { get() { return readabilityHighlights; } },
    isComposing: { get() { return isComposing; }, set(value) { isComposing = value; } },
    readabilityTimer: { get() { return readabilityTimer; }, set(value) { readabilityTimer = value; } },
    editor: { get() { return editor; } },
    readAloudRate: { get() { return readAloudRate; }, set(value) { readAloudRate = value; } },
    readAloudVoiceUri: { get() { return readAloudVoiceUri; }, set(value) { readAloudVoiceUri = value; } },
    readAloudQueue: { get() { return readAloudQueue; }, set(value) { readAloudQueue = value; } },
    readAloudAt: { get() { return readAloudAt; }, set(value) { readAloudAt = value; } },
    readAloudActive: { get() { return readAloudActive; }, set(value) { readAloudActive = value; } },
    speakingHighlight: { get() { return speakingHighlight; } },
    wrapper: { get() { return wrapper; } },
    compositionEndTimer: { get() { return compositionEndTimer; }, set(value) { compositionEndTimer = value; } },
    isSettingContent: { get() { return isSettingContent; }, set(value) { isSettingContent = value; } },
    lastHtml: { get() { return lastHtml; }, set(value) { lastHtml = value; } },
    savedScrollTop: { get() { return savedScrollTop; }, set(value) { savedScrollTop = value; } },
    PARAGRAPH_STYLE_PREFIX: { get() { return PARAGRAPH_STYLE_PREFIX; } },
    entityIndex: { get() { return entityIndex; }, set(value) { entityIndex = value; } },
    entityMatchRules: { get() { return entityMatchRules; }, set(value) { entityMatchRules = value; } },
    entityNames: { get() { return entityNames; }, set(value) { entityNames = value; } },
    entityRegex: { get() { return entityRegex; }, set(value) { entityRegex = value; } },
    typewriterEnabled: { get() { return typewriterEnabled; }, set(value) { typewriterEnabled = value; } },
    typewriterAnchor: { get() { return typewriterAnchor; }, set(value) { typewriterAnchor = value; } },
    typewriterLastY: { get() { return typewriterLastY; }, set(value) { typewriterLastY = value; } },
    typewriterMouseDown: { get() { return typewriterMouseDown; }, set(value) { typewriterMouseDown = value; } },
    typewriterSuspendUntil: { get() { return typewriterSuspendUntil; }, set(value) { typewriterSuspendUntil = value; } },
    typewriterFrame: { get() { return typewriterFrame; }, set(value) { typewriterFrame = value; } },
    programmaticScrollUntil: { get() { return programmaticScrollUntil; }, set(value) { programmaticScrollUntil = value; } },
    mentionCandidates: { get() { return mentionCandidates; }, set(value) { mentionCandidates = value; } },
    mentionState: { get() { return mentionState; }, set(value) { mentionState = value; } },
    mentionCreateVisible: { get() { return mentionCreateVisible; }, set(value) { mentionCreateVisible = value; } },
    mentionFiltered: { get() { return mentionFiltered; }, set(value) { mentionFiltered = value; } },
    mentionSelectedIndex: { get() { return mentionSelectedIndex; }, set(value) { mentionSelectedIndex = value; } },
    mentionPendingSeq: { get() { return mentionPendingSeq; }, set(value) { mentionPendingSeq = value; } },
    autoReplacements: { get() { return autoReplacements; }, set(value) { autoReplacements = value; } },
    dialogueCorrectionConfig: { get() { return dialogueCorrectionConfig; }, set(value) { dialogueCorrectionConfig = value; } },
    dialogueCorrectionTimer: { get() { return dialogueCorrectionTimer; }, set(value) { dialogueCorrectionTimer = value; } },
    entityExitTimer: { get() { return entityExitTimer; }, set(value) { entityExitTimer = value; } },
    lastHoveredMentionId: { get() { return lastHoveredMentionId; }, set(value) { lastHoveredMentionId = value; } },
    lastHoveredAlias: { get() { return lastHoveredAlias; }, set(value) { lastHoveredAlias = value; } },
    isFocusing: { get() { return isFocusing; }, set(value) { isFocusing = value; } }
});
