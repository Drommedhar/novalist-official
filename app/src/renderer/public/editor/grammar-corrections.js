'use strict';

function showGrammarPopup(issueIndex, x, y) {
    const issue = window.NovalistEditorState.grammarIssues[issueIndex];
    if (!issue) return;

    const msgEl = window.NovalistEditorState.grammarPopup.querySelector('.gp-message');
    const sugEl = window.NovalistEditorState.grammarPopup.querySelector('.gp-suggestions');

    msgEl.textContent = issue.message || 'Issue detected';
    sugEl.innerHTML = '';

    if (issue.replacements && issue.replacements.length > 0) {
        for (const rep of issue.replacements) {
            const btn = document.createElement('span');
            btn.className = 'gp-suggestion';
            btn.textContent = rep || window.NovalistEditorState.contextMenuLabels.removeText || 'Delete';
            btn.addEventListener('click', () => {
                applyGrammarSuggestion(issueIndex, rep);
                hideGrammarPopup();
            });
            sugEl.appendChild(btn);
        }
    }

    if (issue.type === 'spelling') {
        const spans = window.NovalistEditorState.editor.querySelectorAll('[data-issue-index="' + issueIndex + '"]');
        if (spans.length > 0) {
            const word = issue.text || '';
            if (word.trim().length > 0) {
                const addBtn = document.createElement('span');
                addBtn.className = 'gp-suggestion';
                addBtn.textContent = window.NovalistEditorState.contextMenuLabels.addToDictionary || 'Add to Dictionary';
                addBtn.style.opacity = '0.7';
                addBtn.addEventListener('click', () => {
                    window.sendMessage({ type: 'addToDictionary', word: word.trim() });
                    hideGrammarPopup();
                });
                sugEl.appendChild(addBtn);
            }
        }
    }

    // Position popup
    window.NovalistEditorState.grammarPopup.style.left = Math.max(4, Math.min(x, window.innerWidth - 340)) + 'px';
    window.NovalistEditorState.grammarPopup.style.top = (y + 20) + 'px';
    window.NovalistEditorState.grammarPopup.classList.add('visible');
    // Flip above the word if it would overflow the bottom (e.g. a low line).
    const ph = window.NovalistEditorState.grammarPopup.offsetHeight;
    if (y + 20 + ph > window.innerHeight) {
        window.NovalistEditorState.grammarPopup.style.top = Math.max(4, y - ph - 8) + 'px';
    }
}

function hideGrammarPopup() {
    window.NovalistEditorState.grammarPopup.classList.remove('visible');
}

function applyGrammarSuggestion(issueIndex, replacement) {
    const issue = window.NovalistEditorState.grammarIssues[issueIndex];
    const range = grammarIssueRange(issueIndex);
    if (!issue || !range || range.toString() !== issue.text) {
        requestGrammarCheck();
        return;
    }
    const spans = window.NovalistEditorState.editor.querySelectorAll('[data-issue-index="' + issueIndex + '"]');

    // Strip grammar-issue classes and attributes first so the browser does not
    // preserve the underline style when replacing the elements via insertText.
    for (const span of spans) {
        span.className = '';
        span.removeAttribute('style');
        span.removeAttribute('data-issue-type');
        span.removeAttribute('data-issue-index');
    }

    // One range covers every fragment, including intervening inline markup.
    // A sentence correction is one native undo step, just like typing over it.
    if (!replaceProofingRange(range, issue.text, replacement)) return;

    // Remove the issue from the list. The input event fired by execCommand
    // takes care of the contentChanged notification and the grammar recheck.
    window.NovalistEditorState.grammarIssues.splice(issueIndex, 1);
    for (const mark of window.NovalistEditorState.editor.querySelectorAll('.grammar-issue')) {
        const index = Number(mark.dataset.issueIndex);
        if (index > issueIndex) mark.dataset.issueIndex = index - 1;
    }
    window.updateGrammarStatusBar();
}

function grammarIssueRange(issueIndex) {
    const spans = window.NovalistEditorState.editor.querySelectorAll('.grammar-issue[data-issue-index="' + issueIndex + '"]');
    if (!spans.length) return null;
    const range = document.createRange();
    range.setStartBefore(spans[0]);
    range.setEndAfter(spans[spans.length - 1]);
    return range;
}

function replaceProofingRange(range, expectedText, replacement) {
    if (!range || range.collapsed || !window.NovalistEditorState.editor.contains(range.startContainer)
        || !window.NovalistEditorState.editor.contains(range.endContainer) || range.toString() !== expectedText) return false;
    const scrollTop = window.NovalistEditorState.wrapper.scrollTop;
    window.NovalistEditorState.editor.focus({ preventScroll: true });
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(range);
    const applied = document.execCommand(replacement === '' ? 'delete' : 'insertText', false, replacement);
    // A correction restores focus, but must not move the line out from under
    // the popup on the input handler's next animation frame.
    if (window.NovalistEditorState.typewriterFrame) cancelAnimationFrame(window.NovalistEditorState.typewriterFrame);
    window.NovalistEditorState.typewriterFrame = 0;
    window.NovalistEditorState.wrapper.scrollTop = scrollTop;
    return applied;
}

function requestGrammarCheck() {
    if (window.NovalistEditorState.isComposing) return;
    window.logToHost("requestGrammarCheck called: grammarEnabled=" + window.NovalistEditorState.grammarEnabled + ", grammarChecking=" + window.NovalistEditorState.grammarChecking);
    const requestId = ++window.NovalistEditorState.grammarRequestId;
    window.NovalistEditorState.pendingGrammarRequest = null;
    if (window.NovalistEditorState.grammarCheckTimer) clearTimeout(window.NovalistEditorState.grammarCheckTimer);
    window.NovalistEditorState.grammarCheckTimer = null;
    if (!window.NovalistEditorState.grammarEnabled) return;
    if (window.NovalistEditorState.grammarChecking) {
        window.NovalistEditorState.grammarChecking = false;
        window.updateGrammarStatusBar();
    }
    window.NovalistEditorState.grammarCheckTimer = setTimeout(() => {
        window.NovalistEditorState.grammarCheckTimer = null;
        // Use buildGrammarPlainTextMap so the text we send and the offsets we
        // get back stay aligned with our highlight-positioning logic.
        const { plainText } = window.buildGrammarPlainTextMap();
        window.NovalistEditorState.pendingGrammarRequest = { id: requestId, text: plainText };
        window.NovalistEditorState.grammarChecking = true;
        window.logToHost("requestGrammarCheck timer fired: grammarChecking set to true, sending request. Plain text len=" + plainText.length);
        window.updateGrammarStatusBar();
        window.sendMessage({
            type: 'grammarCheckRequest',
            requestId: requestId,
            plainText: plainText
        });
    }, 1500);
}

Object.assign(window, {
    showGrammarPopup,
    hideGrammarPopup,
    applyGrammarSuggestion,
    grammarIssueRange,
    replaceProofingRange,
    requestGrammarCheck
});
