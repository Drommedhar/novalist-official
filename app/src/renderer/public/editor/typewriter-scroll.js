'use strict';

function setComposeDimming(enabled) {
    window.NovalistEditorState.dimOthers = !!enabled;
    document.body.classList.toggle('dim-others', window.NovalistEditorState.dimOthers);
    updateFocusBlock();
}

function updateFocusBlock() {
    for (const block of window.proseBlocks()) block.classList.remove('nv-focus-block');
    if (!window.NovalistEditorState.dimOthers) return;
    for (const block of window.selectedBlocks()) block.classList.add('nv-focus-block');
}

function setTypewriterScroll(enabled, anchor) {
    const nextAnchor = (anchor === 'top' || anchor === 'bottom') ? anchor : 'middle';
    if (window.NovalistEditorState.typewriterEnabled === !!enabled && window.NovalistEditorState.typewriterAnchor === nextAnchor) return;
    window.NovalistEditorState.typewriterEnabled = !!enabled;
    window.NovalistEditorState.typewriterAnchor = nextAnchor;
    document.body.classList.toggle('typewriter-on', window.NovalistEditorState.typewriterEnabled);
    window.NovalistEditorState.typewriterLastY = -9999;
    scheduleTypewriterRecenter();
}

function scheduleTypewriterRecenter() {
    if (window.NovalistEditorState.typewriterMouseDown) return;                       // user drag-selecting
    if (Date.now() < window.NovalistEditorState.typewriterSuspendUntil) return;       // recently wheel-scrolled
    if (window.NovalistEditorState.typewriterFrame) return;
    window.NovalistEditorState.typewriterFrame = requestAnimationFrame(() => {
        window.NovalistEditorState.typewriterFrame = 0;
        recenterCaret();
    });
}

function recenterCaret() {
    if (window.NovalistEditorState.typewriterMouseDown || Date.now() < window.NovalistEditorState.typewriterSuspendUntil) return;
    const sel = window.getSelection();
    if (!window.isCaretInEditor(sel)) return;
    const range = sel.getRangeAt(0);

    // Measure caret without mutating the DOM — any insertNode/removeChild here
    // would split text nodes around the user's selection and interfere with input.
    let rect = range.getBoundingClientRect();
    let top = rect.top;
    let bottom = rect.bottom;
    if (top === 0 && bottom === 0) {
        // Empty block: fall back to the nearest element's rect.
        const node = range.startContainer;
        const el = node && node.nodeType === Node.ELEMENT_NODE ? node : (node && node.parentElement);
        if (!el) return;
        const elRect = el.getBoundingClientRect();
        if (elRect.height === 0 && elRect.width === 0) return;
        top = elRect.top;
        bottom = elRect.bottom;
    }

    const wrapperRect = window.NovalistEditorState.wrapper.getBoundingClientRect();
    const viewportH = window.NovalistEditorState.wrapper.clientHeight;

    if (window.NovalistEditorState.typewriterEnabled) {
        if (Math.abs(top - window.NovalistEditorState.typewriterLastY) < 4) return;
        window.NovalistEditorState.typewriterLastY = top;

        let frac = 0.5;
        if (window.NovalistEditorState.typewriterAnchor === 'top') frac = 0.33;
        else if (window.NovalistEditorState.typewriterAnchor === 'bottom') frac = 0.66;
        const targetY = viewportH * frac;
        const caretRelative = top - wrapperRect.top;
        const delta = caretRelative - targetY;
        if (Math.abs(delta) < 2) return;
        window.NovalistEditorState.programmaticScrollUntil = Date.now() + 150;
        window.NovalistEditorState.wrapper.scrollTop = window.NovalistEditorState.wrapper.scrollTop + delta;
    } else {
        // Typewriter is disabled: just keep caret in view (standard scroll-into-view)
        const caretRelativeTop = top - wrapperRect.top;
        const caretRelativeBottom = bottom - wrapperRect.top;
        const padding = 40; // px margin

        let delta = 0;
        if (caretRelativeTop < padding) {
            delta = caretRelativeTop - padding;
        } else if (caretRelativeBottom > viewportH - padding) {
            delta = caretRelativeBottom - (viewportH - padding);
        }

        if (Math.abs(delta) > 1) {
            window.NovalistEditorState.programmaticScrollUntil = Date.now() + 150;
            window.NovalistEditorState.wrapper.scrollTop = window.NovalistEditorState.wrapper.scrollTop + delta;
        }
    }
}

Object.assign(window, {
    setComposeDimming,
    updateFocusBlock,
    setTypewriterScroll,
    scheduleTypewriterRecenter,
    recenterCaret
});
