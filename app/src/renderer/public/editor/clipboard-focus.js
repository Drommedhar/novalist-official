'use strict';

function isTextEditingShortcut(e) {
    // Cmd is the editing modifier on Apple platforms, so it must reserve the same
    // native shortcuts (Cmd+C/V/X/Z/A) that Ctrl does elsewhere.
    if (!e.ctrlKey && !e.metaKey) return false;
    var k = e.key.toLowerCase();
    // Navigation keys that should be handled natively by contenteditable:
    // Ctrl+Arrow walks by word, Ctrl+Shift+Arrow selects by word, Ctrl+Home and
    // Ctrl+End go to the ends. Alt is the exception - contenteditable does
    // nothing with Ctrl+Alt+Arrow, and reserving it anyway meant the pane
    // splitting gestures died the moment the caret was in the prose, which is
    // the only place a writer ever presses them from.
    var navKeys = ['arrowleft', 'arrowright', 'arrowup', 'arrowdown', 'home', 'end'];
    if (navKeys.includes(k)) return !e.altKey;
    // Ctrl+(no shift): select-all, copy, paste, cut, undo, redo. On Apple
    // keyboards Cmd+B/I/U are the system bold/italic/underline gestures and
    // contenteditable applies them natively, so they stay reserved: the result
    // is the same mark the host would have applied, by the route a Mac writer
    // expects. Ctrl+B/I/U elsewhere are forwarded and the host applies them.
    if (!e.shiftKey && !e.altKey) {
        return 'acvxzy'.includes(k) || (e.metaKey && !e.ctrlKey && 'biu'.includes(k));
    }
    // Ctrl+Shift+Z = redo
    if (e.shiftKey && !e.altKey && k === 'z') return true;
    return false;
}

function sanitizePastedHtml(html) {
    const tmp = document.createElement('div');
    tmp.replaceChildren(window.sanitizeSceneFragment(html));

    // Remove all style attributes except basic formatting
    const allElements = tmp.querySelectorAll('*');
    for (const el of allElements) {
        // Keep b, i, u, strong, em, p, br, div, span
        const tag = el.tagName.toLowerCase();
        const allowedTags = ['b', 'i', 'u', 'strong', 'em', 'p', 'br', 'div', 'span', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6'];
        if (!allowedTags.includes(tag)) {
            // Replace with its text content
            el.replaceWith(...el.childNodes);
            continue;
        }
        // Remove all attributes except style with basic formatting
        const style = el.getAttribute('style');
        // Clear all attributes
        while (el.attributes.length > 0) el.removeAttribute(el.attributes[0].name);
        if (style) {
            // Only keep font-weight, font-style, text-decoration, text-align
            const allowed = {};
            if (/font-weight\s*:\s*bold/i.test(style)) allowed['font-weight'] = 'bold';
            if (/font-style\s*:\s*italic/i.test(style)) allowed['font-style'] = 'italic';
            if (/text-decoration\s*:\s*underline/i.test(style)) allowed['text-decoration'] = 'underline';
            const alignMatch = style.match(/text-align\s*:\s*(left|center|right|justify)/i);
            if (alignMatch) allowed['text-align'] = alignMatch[1];
            const parts = Object.entries(allowed).map(([k, v]) => k + ':' + v);
            if (parts.length > 0) el.setAttribute('style', parts.join(';'));
        }
    }
    return tmp.innerHTML;
}

function focusEditor() {
    window.NovalistEditorState.editor.focus({ preventScroll: true });
}

Object.assign(window, {
    isTextEditingShortcut,
    sanitizePastedHtml,
    focusEditor
});
