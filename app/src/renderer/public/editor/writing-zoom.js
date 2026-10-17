'use strict';

(() => {
    const wrapper = document.querySelector('#editor-wrapper, #manuscript-wrapper');
    const content = document.querySelector('#editor, #manuscript-content');
    const surface = document.createElement('div');
    surface.className = 'writing-zoom-surface';
    content.before(surface);
    surface.append(content);
    let scale = 1;
    let appliedScale = 1;
    let layoutWidth = 0;
    let layoutKey = '';
    let scheduled = false;
    let typewriterAnchor = null;
    let caretAnchor = null;

    function caretPosition() {
        const selection = window.getSelection();
        if (!document.hasFocus() || !selection?.rangeCount || !selection.isCollapsed ||
            !content.contains(selection.focusNode) || !content.contains(document.activeElement)) return null;
        const range = selection.getRangeAt(0);
        let rect = range.getBoundingClientRect();
        if (!rect.height) {
            const node = selection.focusNode;
            const element = node.nodeType === Node.ELEMENT_NODE ? node : node.parentElement;
            rect = element.getBoundingClientRect();
        }
        if (!rect.height) return null;
        return { node: selection.focusNode, offset: selection.focusOffset, rect };
    }

    function captureCaretAnchor() {
        const caret = caretPosition();
        if (!caret) return null;
        // Repeated wheel steps must not accumulate scroll rounding error.
        if (caretAnchor?.node === caret.node && caretAnchor.offset === caret.offset) return caretAnchor;
        const viewport = wrapper.getBoundingClientRect();
        return { node: caret.node, offset: caret.offset, x: caret.rect.left - viewport.left, y: caret.rect.top - viewport.top };
    }

    function restoreCaretAnchor() {
        if (!caretAnchor) return;
        const caret = caretPosition();
        if (!caret || caret.node !== caretAnchor.node || caret.offset !== caretAnchor.offset) {
            caretAnchor = null;
            return;
        }
        const viewport = wrapper.getBoundingClientRect();
        const margin = 40;
        const x = Math.max(margin, Math.min(caretAnchor.x, wrapper.clientWidth - margin));
        const fraction = { top: 0.33, middle: 0.5, bottom: 0.66 }[typewriterAnchor];
        const y = fraction ? wrapper.clientHeight * fraction
            : Math.max(margin, Math.min(caretAnchor.y, wrapper.clientHeight - caret.rect.height - margin));
        if (window.NovalistEditorState) window.NovalistEditorState.programmaticScrollUntil = Date.now() + 150;
        wrapper.scrollTop += caret.rect.top - viewport.top - y;
        wrapper.scrollLeft += caret.rect.left - viewport.left - x;
    }

    function clearLayout() {
        content.style.removeProperty('transform');
        content.style.removeProperty('width');
        surface.style.removeProperty('width');
        surface.style.removeProperty('height');
        wrapper.style.removeProperty('max-width');
        wrapper.style.removeProperty('--writing-layout-width');
    }

    function resizeSurface() {
        if (scale !== 1) surface.style.height = content.getBoundingClientRect().height + 'px';
    }

    function refresh() {
        const root = getComputedStyle(document.documentElement);
        const key = ['--book-width', '--font-size', '--font-family'].map(name => root.getPropertyValue(name)).join('|') +
            ['page-view', 'has-comments', 'mobile'].map(name => document.body.classList.contains(name)).join('|');
        const scrollTop = wrapper.scrollTop;
        const scrollLeft = wrapper.scrollLeft;
        const ratio = scale / appliedScale;
        if (scale === 1) {
            clearLayout();
            layoutWidth = 0;
        } else {
            // Keep the unzoomed measure while the zoomed viewport grows or shrinks.
            if (!layoutWidth || key !== layoutKey) {
                clearLayout();
                layoutWidth = wrapper.getBoundingClientRect().width - (wrapper.offsetWidth - wrapper.clientWidth);
            }
            const scrollbar = wrapper.offsetWidth - wrapper.clientWidth;
            wrapper.style.maxWidth = (layoutWidth * scale + scrollbar) + 'px';
            wrapper.style.setProperty('--writing-layout-width', layoutWidth + 'px');
            surface.style.width = (layoutWidth * scale) + 'px';
            content.style.width = layoutWidth + 'px';
            // CSS zoom reshapes glyphs at the scaled font size and can rewrap lines.
            content.style.transform = 'scale(' + scale + ')';
        }
        content.style.minHeight = (wrapper.clientHeight / scale) + 'px';
        resizeSurface();
        layoutKey = key;
        appliedScale = scale;
        wrapper.scrollTop = scrollTop * ratio;
        wrapper.scrollLeft = scrollLeft * ratio;
        restoreCaretAnchor();
        window.scheduleCommentRender?.();
        window.schedulePageViewRepaginate?.();
    }

    function scheduleRefresh() {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(() => {
            scheduled = false;
            refresh();
        });
    }

    window.setWritingZoom = (factor, anchor = typewriterAnchor) => {
        if (!Number.isFinite(factor) || factor <= 0) return;
        typewriterAnchor = anchor == null ? null : (anchor === 'top' || anchor === 'bottom' ? anchor : 'middle');
        if (factor !== scale) caretAnchor = captureCaretAnchor();
        scale = factor;
        content.style.setProperty('--writing-zoom-scale', String(scale));
        document.body.classList.toggle('zoom-typewriter', typewriterAnchor !== null);
        refresh();
    };
    // Pagination and Focus Mode can finish resizing after the zoom message.
    // Keep the anchor until the writer scrolls, edits or moves the caret.
    window.restoreWritingZoomCaret = restoreCaretAnchor;
    const clearCaretAnchor = () => { caretAnchor = null; };
    for (const event of ['pointerdown', 'keydown', 'input']) {
        document.addEventListener(event, clearCaretAnchor, true);
    }
    document.addEventListener('wheel', event => {
        if (!event.ctrlKey) clearCaretAnchor();
    }, { passive: true, capture: true });
    const observer = new MutationObserver(scheduleRefresh);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['style'] });
    observer.observe(document.body, { attributes: true, attributeFilter: ['class'] });
    new ResizeObserver(resizeSurface).observe(content);
    window.addEventListener('resize', scheduleRefresh);
})();
