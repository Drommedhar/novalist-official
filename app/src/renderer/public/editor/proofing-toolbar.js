'use strict';

function updateGrammarStatusBar() {
    if (!window.NovalistEditorState.grammarStatusBar) return;
    window.logToHost("updateGrammarStatusBar: grammarEnabled=" + window.NovalistEditorState.grammarEnabled + ", grammarChecking=" + window.NovalistEditorState.grammarChecking + ", issues count=" + window.NovalistEditorState.grammarIssues.length);

    if (!window.NovalistEditorState.grammarEnabled) {
        window.NovalistEditorState.grammarStatusBar.classList.remove('visible');
        return;
    }

    window.NovalistEditorState.grammarStatusBar.classList.add('visible');

    const throbber = document.getElementById('status-throbber');
    const doneIcon = document.getElementById('status-check-done');
    const grammarItem = document.getElementById('status-grammar-count-container');
    const punctuationItem = document.getElementById('status-punctuation-count-container');
    const grammarCountSpan = document.getElementById('status-grammar-count');
    const punctuationCountSpan = document.getElementById('status-punctuation-count');

    // Throbber state
    if (window.NovalistEditorState.grammarChecking) {
        throbber.classList.add('active');
        doneIcon.classList.remove('active');
    } else {
        throbber.classList.remove('active');
    }

    // Compute counts
    const grammarCount = window.NovalistEditorState.grammarIssues.filter(i => i.type === 'grammar').length;
    const punctuationCount = window.NovalistEditorState.grammarIssues.filter(i => i.type === 'spelling' || i.type === 'style').length;

    // Show checkmark done only when not checking and counts are 0
    if (!window.NovalistEditorState.grammarChecking && grammarCount === 0 && punctuationCount === 0) {
        doneIcon.classList.add('active');
    } else {
        doneIcon.classList.remove('active');
    }

    if (grammarCount > 0) {
        grammarCountSpan.textContent = grammarCount;
        grammarItem.classList.add('active');
    } else {
        grammarItem.classList.remove('active');
    }

    if (punctuationCount > 0) {
        punctuationCountSpan.textContent = punctuationCount;
        punctuationItem.classList.add('active');
    } else {
        punctuationItem.classList.remove('active');
    }
}

function scrollToNextIssue(type) {
    const selector = type === 'grammar'
        ? '.grammar-issue[data-issue-type="grammar"]'
        : '.grammar-issue[data-issue-type="spelling"], .grammar-issue[data-issue-type="style"]';
    const firstIssue = window.NovalistEditorState.editor.querySelector(selector);
    if (firstIssue) {
        firstIssue.scrollIntoView({ behavior: 'smooth', block: 'center' });
        // Wait briefly for scroll to finish, then open popup
        setTimeout(() => {
            const index = parseInt(firstIssue.getAttribute('data-issue-index'), 10);
            const rect = firstIssue.getBoundingClientRect();
            window.showGrammarPopup(index, rect.left, rect.bottom);
        }, 150);
    }
}

function updateFloatingToolbar() {
    // Right-clicking a word selects it so the menu can offer spellings for it,
    // and that selection used to raise the format bar on top of the very menu
    // the click had just opened - the bar sits a layer above it. The menu is
    // what was asked for, so the bar waits until it closes.
    if (window.NovalistEditorState.contextMenu.classList.contains('visible')) {
        window.NovalistEditorState.floatingToolbar.classList.remove('visible');
        return;
    }
    const sel = window.getSelection();
    if (!sel || sel.isCollapsed || sel.toString().trim() === '') {
        window.NovalistEditorState.floatingToolbar.classList.remove('visible');
        return;
    }
    const range = sel.getRangeAt(0);
    const rect = range.getBoundingClientRect();
    if (rect.width === 0 && rect.height === 0) {
        window.NovalistEditorState.floatingToolbar.classList.remove('visible');
        return;
    }

    document.getElementById('ft-bold').classList.toggle('active', document.queryCommandState('bold'));
    document.getElementById('ft-italic').classList.toggle('active', document.queryCommandState('italic'));
    document.getElementById('ft-underline').classList.toggle('active', document.queryCommandState('underline'));
    document.getElementById('ft-strike').classList.toggle('active', document.queryCommandState('strikeThrough'));
    document.getElementById('ft-highlight').classList.toggle('active',
        !!window.highlightAncestor(window.getSelection() && window.getSelection().anchorNode));

    window.NovalistEditorState.floatingToolbar.classList.add('visible');
    const tbWidth = window.NovalistEditorState.floatingToolbar.offsetWidth || 220;
    const tbHeight = window.NovalistEditorState.floatingToolbar.offsetHeight || 34;
    let x = rect.left + rect.width / 2 - tbWidth / 2;
    let y = rect.top - tbHeight - 8;
    if (x < 4) x = 4;
    if (x + tbWidth > window.innerWidth - 4) x = window.innerWidth - tbWidth - 4;
    if (y < 4) y = rect.bottom + 8;
    window.NovalistEditorState.floatingToolbar.style.left = x + 'px';
    window.NovalistEditorState.floatingToolbar.style.top = y + 'px';
}

Object.assign(window, {
    updateGrammarStatusBar,
    scrollToNextIssue,
    updateFloatingToolbar
});
