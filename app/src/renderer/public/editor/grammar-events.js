'use strict';

let grammarIssues = [];

let grammarCheckTimer = null;

let grammarEnabled = false;

let grammarChecking = false;

let grammarRequestId = 0;

let pendingGrammarRequest = null;

const grammarPopup = document.getElementById('grammar-popup');

const grammarStatusBar = document.getElementById('grammar-status-bar');

grammarPopup.addEventListener('mousedown', (e) => e.preventDefault());

// Hook up status bar event listeners
document.getElementById('status-grammar-count-container').addEventListener('click', (e) => {
    window.scrollToNextIssue('grammar');
    e.stopPropagation();
});

document.getElementById('status-punctuation-count-container').addEventListener('click', (e) => {
    window.scrollToNextIssue('punctuation');
    e.stopPropagation();
});

// Grammar issue click/hover handler
window.NovalistEditorState.editor.addEventListener('click', (e) => {
    const issueSpan = e.target.closest('.grammar-issue');
    if (issueSpan) {
        const index = parseInt(issueSpan.getAttribute('data-issue-index'), 10);
        const rect = issueSpan.getBoundingClientRect();
        window.showGrammarPopup(index, rect.left, rect.bottom);
        e.stopPropagation();
    } else {
        window.hideGrammarPopup();
    }
});

// Dismiss button
grammarPopup.querySelector('.gp-dismiss').addEventListener('click', window.hideGrammarPopup);

// Hide popup on scroll or outside click
window.NovalistEditorState.wrapper.addEventListener('scroll', window.hideGrammarPopup);

document.addEventListener('click', (e) => {
    if (!grammarPopup.contains(e.target) && !e.target.closest('.grammar-issue')) {
        window.hideGrammarPopup();
    }
});

// Mobile: a plain tap on a grammar issue or an entity mention would place the
// caret and raise the on-screen keyboard (native contenteditable). Intercept on
// mousedown and preventDefault (the same trick the floating toolbar uses to keep
// the selection) so the tap instead shows the grammar suggestions / entity peek
// without moving the caret. Touch has no hover, so this is how peek is reachable.
window.NovalistEditorState.editor.addEventListener('mousedown', (e) => {
    if (!document.body.classList.contains('mobile')) return;
    const t = e.target;
    if (!t || !t.closest) return;
    const issue = t.closest('.grammar-issue');
    if (issue) {
        e.preventDefault();
        const index = parseInt(issue.getAttribute('data-issue-index'), 10);
        const rect = issue.getBoundingClientRect();
        window.showGrammarPopup(index, rect.left, rect.bottom);
        return;
    }
    const mention = t.closest('.nv-entity-mention');
    if (mention) {
        e.preventDefault();
        const id = mention.getAttribute('data-entity-id') || '';
        const rect = mention.getBoundingClientRect();
        if (id) window.sendMessage({
            type: 'entityMentionHover',
            entityId: id,
            x: rect.left,
            y: rect.bottom,
            rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom }
        });
        return;
    }
    // Most entity references in prose are plain text (auto-detected), not explicit
    // mention spans, so resolve the entity at the tap point and peek that.
    const tapped = (typeof window.findEntityAtPoint === 'function') ? window.findEntityAtPoint(e.clientX, e.clientY) : null;
    if (tapped) {
        e.preventDefault();
        window.sendMessage({
            type: 'entityHover',
            alias: tapped.alias,
            x: e.clientX,
            y: e.clientY,
            rect: tapped.rect
        });
        return;
    }
    // Tapping plain text with no entity dismisses any shown peek.
    window.sendMessage({ type: 'entityExit' });
});

Object.defineProperties(window.NovalistEditorState, {
    grammarIssues: { get() { return grammarIssues; }, set(value) { grammarIssues = value; } },
    grammarEnabled: { get() { return grammarEnabled; }, set(value) { grammarEnabled = value; } },
    grammarRequestId: { get() { return grammarRequestId; }, set(value) { grammarRequestId = value; } },
    pendingGrammarRequest: { get() { return pendingGrammarRequest; }, set(value) { pendingGrammarRequest = value; } },
    grammarCheckTimer: { get() { return grammarCheckTimer; }, set(value) { grammarCheckTimer = value; } },
    grammarChecking: { get() { return grammarChecking; }, set(value) { grammarChecking = value; } },
    grammarPopup: { get() { return grammarPopup; } },
    grammarStatusBar: { get() { return grammarStatusBar; } }
});
