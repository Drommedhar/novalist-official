'use strict';

function setMobile(enabled) {
    // Full-width text (drop the margin comment gutter) + touch-sized controls.
    document.body.classList.toggle('mobile', !!enabled);
}

function setPageView(enabled) {
    const wasEnabled = window.NovalistEditorState.pageViewEnabled;
    window.NovalistEditorState.pageViewEnabled = !!enabled;
    document.body.classList.toggle('page-view', window.NovalistEditorState.pageViewEnabled);
    if (window.NovalistEditorState.pageViewEnabled) {
        if (!window.NovalistEditorState.pageViewMutationObserver) {
            window.NovalistEditorState.pageViewMutationObserver = new MutationObserver(() => {
                if (window.NovalistEditorState.pageViewRepaginating) return;
                schedulePageViewRepaginate();
            });
        }
        window.NovalistEditorState.pageViewMutationObserver.observe(window.NovalistEditorState.editor, { childList: true, characterData: true, subtree: true });
        // Apply the paper and its padding together. Deferring the first wrap
        // leaves already-visible prose jumping when the debounce later fires.
        // Further edits/settings keep their debounce; active composition is
        // still protected by repaginatePageView's composition guard.
        if (wasEnabled) schedulePageViewRepaginate();
        else repaginatePageView();
    } else {
        if (window.NovalistEditorState.pageViewMutationObserver) {
            window.NovalistEditorState.pageViewMutationObserver.disconnect();
        }
        const savedDictation = window.captureDictationPosition();
        unwrapPages();
        window.restoreDictationPosition(savedDictation);
    }
}

function schedulePageViewRepaginate() {
    if (!window.NovalistEditorState.pageViewEnabled || window.NovalistEditorState.isComposing) return;
    if (window.NovalistEditorState.pageViewRepaginateScheduled) return;
    window.NovalistEditorState.pageViewRepaginateScheduled = true;
    setTimeout(() => {
        window.NovalistEditorState.pageViewRepaginateScheduled = false;
        repaginatePageView();
    }, 150);
}

function captureSelectionForRepaginate() {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return null;
    const r = sel.getRangeAt(0);
    if (!window.NovalistEditorState.editor.contains(r.startContainer)) return null;
    // Snapshot the live (node, offset) pair. Unwrap/rewrap of .nv-page wrappers
    // only moves these nodes around; the references stay valid, so restoring
    // them preserves the caret exactly — including in empty paragraphs that a
    // text-only walker would skip past.
    return {
        startContainer: r.startContainer,
        startOffset: r.startOffset,
        endContainer: r.endContainer,
        endOffset: r.endOffset,
    };
}

function restoreSelectionForRepaginate(saved) {
    if (!saved) return;
    if (!window.NovalistEditorState.editor.contains(saved.startContainer) || !window.NovalistEditorState.editor.contains(saved.endContainer)) return;
    const r = document.createRange();
    try {
        r.setStart(saved.startContainer, saved.startOffset);
        r.setEnd(saved.endContainer, saved.endOffset);
    } catch {
        return;
    }
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(r);
}

function unwrapPages() {
    const pages = Array.from(window.NovalistEditorState.editor.querySelectorAll(':scope > .nv-page'));
    if (pages.length === 0) return;
    pages.forEach(page => {
        while (page.firstChild) window.NovalistEditorState.editor.insertBefore(page.firstChild, page);
        page.remove();
    });
}

function repaginatePageView() {
    if (!window.NovalistEditorState.pageViewEnabled || window.NovalistEditorState.isComposing) return;
    window.NovalistEditorState.pageViewRepaginating = true;
    if (window.NovalistEditorState.pageViewMutationObserver) window.NovalistEditorState.pageViewMutationObserver.disconnect();
    const savedDictation = window.captureDictationPosition();
    try {
        const savedSelection = captureSelectionForRepaginate();

        // Unwrap any existing pages so we can re-measure flat content.
        unwrapPages();

        const children = Array.from(window.NovalistEditorState.editor.children);
        if (children.length === 0) return;

        // Force layout, then snapshot heights so wrapping does not invalidate
        // measurements mid-pass.
        void window.NovalistEditorState.editor.offsetHeight;
        const heights = children.map(el => Math.max(1, el.offsetHeight));

        // Content budget per page measured in current em so it scales with font / zoom.
        const emPx = parseFloat(getComputedStyle(window.NovalistEditorState.editor).fontSize) || 16;
        const pageContentHeight = 48 * emPx;

        // Compute groups by summing heights.
        const groups = [];
        let current = [];
        let currentHeight = 0;
        for (let i = 0; i < children.length; i++) {
            const h = heights[i];
            if (currentHeight + h > pageContentHeight && current.length > 0) {
                groups.push(current);
                current = [];
                currentHeight = 0;
            }
            current.push(children[i]);
            currentHeight += h;
        }
        if (current.length > 0) groups.push(current);

        // Wrap each group in a .nv-page container.
        groups.forEach(group => {
            const page = document.createElement('div');
            page.className = 'nv-page';
            const first = group[0];
            window.NovalistEditorState.editor.insertBefore(page, first);
            group.forEach(el => page.appendChild(el));
        });

        restoreSelectionForRepaginate(savedSelection);
    } finally {
        window.restoreDictationPosition(savedDictation);
        window.NovalistEditorState.pageViewRepaginating = false;
        if (window.NovalistEditorState.pageViewMutationObserver && window.NovalistEditorState.pageViewEnabled) {
            window.NovalistEditorState.pageViewMutationObserver.observe(window.NovalistEditorState.editor, { childList: true, characterData: true, subtree: true });
        }
        // Every paragraph just moved into a new page wrapper, which collapses
        // the readability ranges that were inside them.
        window.reapplyReadability();
    }
}

Object.assign(window, {
    setMobile,
    setPageView,
    schedulePageViewRepaginate,
    captureSelectionForRepaginate,
    restoreSelectionForRepaginate,
    unwrapPages,
    repaginatePageView
});
