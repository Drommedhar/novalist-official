'use strict';

function sendMessage(msg) {
    try {
        const json = JSON.stringify(msg);
        if (typeof window.invokeCSharpAction === 'function') {
            window.invokeCSharpAction(json);
        } else if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(json);
        } else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.webview) {
            window.webkit.messageHandlers.webview.postMessage(json);
        } else if (window.parent && window.parent !== window) {
            // Electron shell: editor runs in a same-origin iframe; the parent
            // page bridges these messages to the backend over JSON-RPC.
            window.parent.postMessage({ novalistEditor: json }, '*');
        }
    } catch (error) { console.warn("editor: sendMessage failed", error instanceof Error ? error.name : typeof error); }
}

// ── Content Management ──────────────────────────────────────────

function setContent(html) {
    html = window.sanitizeSceneHtml(html);
    window.NovalistEditorState.dictationAnchor = null;
    if (window.NovalistEditorState.compositionEndTimer) clearTimeout(window.NovalistEditorState.compositionEndTimer);
    window.NovalistEditorState.compositionEndTimer = null;
    window.NovalistEditorState.isComposing = false;
    window.NovalistEditorState.isSettingContent = true;
    window.hideGrammarPopup();
    window.hideContextMenu();
    window.NovalistEditorState.grammarIssues = [];
    // Captured before the write and compared after it, so the browser has
    // normalised both sides and the comparison is honest.
    const previous = window.NovalistEditorState.editor.innerHTML;
    if (!html || html.trim() === '' || isEmptyHtml(html)) {
        window.NovalistEditorState.editor.innerHTML = '<p><br></p>';
    } else {
        const bodyContent = extractBody(html);
        window.NovalistEditorState.editor.replaceChildren(window.sanitizeSceneFragment(bodyContent));
    }
    refreshSceneBreakClasses(window.NovalistEditorState.editor);
    const changed = window.NovalistEditorState.editor.innerHTML !== previous;
    window.NovalistEditorState.lastHtml = window.NovalistEditorState.editor.innerHTML;
    window.NovalistEditorState.isSettingContent = false;
    window.NovalistEditorState.savedScrollTop = changed ? 0 : window.NovalistEditorState.savedScrollTop;
    window.NovalistEditorState.wrapper.scrollTop = window.NovalistEditorState.savedScrollTop;

    // A stored path is not a URL this document can load; resolve before paint.
    window.toDisplayImages(window.NovalistEditorState.editor);

    // Trigger grammar check for the new content
    window.requestGrammarCheck();
    // A different scene needs its own grading; the old marks belong to prose
    // that is no longer on screen, so they must not be repainted either.
    window.NovalistEditorState.lastReadabilityGrading = null;
    window.clearReadability();
    window.requestReadability(0);

    // If page view is on, paginate synchronously before paint so users never
    // see the unpaginated layout flash.
    if (window.NovalistEditorState.pageViewEnabled) window.repaginatePageView();
}

function getContent() {
    return window.getCleanContentHtml();
}

function getPlainText() {
    return window.NovalistEditorState.editor.innerText || '';
}

/** Marks ornament-only prose blocks so typography never treats them as prose. */
function refreshSceneBreakClasses(root) {
    root.querySelectorAll('p, div').forEach(function (block) {
        // Page wrappers and structural containers are not paragraphs. Only a
        // leaf block can be the ornament the writer typed between scenes.
        if (block.classList.contains('nv-page') || block.querySelector('p, div, h1, h2, h3, h4, h5, h6, blockquote, li')) return;
        const text = block.textContent || '';
        const isBreak = /^[\s\u00a0]*[*\-#•_~](?:[\s\u00a0]*[*\-#•_~])*[\s\u00a0]*$/u.test(text);
        block.classList.toggle('nv-scene-break', isBreak);
    });
}

function extractBody(html) {
    const match = html.match(/<body[^>]*>([\s\S]*)<\/body>/i);
    return match ? match[1] : html;
}

function isEmptyHtml(html) {
    const tmp = document.createElement('div');
    tmp.replaceChildren(window.sanitizeSceneFragment(html));
    return !tmp.textContent || tmp.textContent.trim() === '';
}

Object.assign(window, {
    sendMessage,
    setContent,
    getContent,
    getPlainText,
    refreshSceneBreakClasses,
    extractBody,
    isEmptyHtml
});
