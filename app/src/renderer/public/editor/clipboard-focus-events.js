'use strict';

// Handle paste: strip external formatting, keep only basic formatting
window.NovalistEditorState.editor.addEventListener('paste', (e) => {
    const html = e.clipboardData?.getData('text/html');
    if (html) {
        e.preventDefault();
        const cleaned = window.sanitizePastedHtml(html);
        document.execCommand('insertHTML', false, cleaned);
    }
});

// ── Scroll Zoom (Ctrl+Wheel) ───────────────────────────────────

window.NovalistEditorState.wrapper.addEventListener('wheel', (e) => {
    if (!e.ctrlKey) return;
    e.preventDefault();
    const delta = e.deltaY < 0 ? 1 : -1;
    window.sendMessage({ type: 'zoom', delta: delta });
}, { passive: false });

// ── Focus ───────────────────────────────────────────────────────

// Focus inside this document does not bubble to the host's pane. Report both
// returning to the frame and focusing its prose or annotation fields.
window.addEventListener('focus', () => {
    window.sendMessage({ type: 'focused' });
}, true);

// Track scroll positions for focus-gain scroll jump prevention
window.NovalistEditorState.wrapper.addEventListener('scroll', () => {
    if (!window.NovalistEditorState.isFocusing) {
        window.NovalistEditorState.savedScrollTop = window.NovalistEditorState.wrapper.scrollTop;
    }
});

document.addEventListener('mousedown', () => {
    if (!window.NovalistEditorState.isFocusing) {
        window.NovalistEditorState.savedScrollTop = window.NovalistEditorState.wrapper.scrollTop;
    }
}, true);

// Prevent default scroll-to-top behavior when editor gains focus natively
window.NovalistEditorState.editor.addEventListener('focus', () => {
    window.NovalistEditorState.isFocusing = true;
    const restore = () => {
        if (window.NovalistEditorState.wrapper.scrollTop !== window.NovalistEditorState.savedScrollTop) {
            window.NovalistEditorState.wrapper.scrollTop = window.NovalistEditorState.savedScrollTop;
        }
    };
    restore();
    requestAnimationFrame(restore);
    setTimeout(() => {
        restore();
        window.NovalistEditorState.isFocusing = false;
    }, 100);
});
