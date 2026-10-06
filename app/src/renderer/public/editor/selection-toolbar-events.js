'use strict';

// ── Floating Selection Toolbar ───────────────────────────────────

const floatingToolbar = document.getElementById('floating-toolbar');

let floatingToolbarTimer = null;

document.addEventListener('selectionchange', () => {
    if (floatingToolbarTimer) clearTimeout(floatingToolbarTimer);
    floatingToolbarTimer = setTimeout(window.updateFloatingToolbar, 60);
});

floatingToolbar.addEventListener('mousedown', (e) => {
    e.preventDefault(); // keep selection alive
    const btn = e.target.closest('button');
    if (!btn) return;
    const id = btn.id;
    if (id === 'ft-bold') document.execCommand('bold', false, null);
    else if (id === 'ft-italic') document.execCommand('italic', false, null);
    else if (id === 'ft-underline') document.execCommand('underline', false, null);
    else if (id === 'ft-strike') document.execCommand('strikeThrough', false, null);
    else if (id === 'ft-highlight') window.toggleHighlight();
    // The host owns dialogs, so it asks for the address and calls back.
    else if (id === 'ft-link') window.sendMessage({ type: 'requestLink' });
    else if (id === 'ft-comment') window.sendMessage({ type: 'requestAddComment' });
    else if (id === 'ft-footnote') window.sendMessage({ type: 'requestAddFootnote' });
    window.updateFloatingToolbar();
});

Object.defineProperties(window.NovalistEditorState, {
    floatingToolbar: { get() { return floatingToolbar; } }
});
