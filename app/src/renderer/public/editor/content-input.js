'use strict';

function setImageBase(base) {
    window.NovalistEditorState.imageBaseUrl = base || '';
    toDisplayImages(window.NovalistEditorState.editor);
}

/** Rewrites stored paths to display URLs, keeping the stored one on the node. */
function toDisplayImages(root) {
    if (!window.NovalistEditorState.imageBaseUrl) return;
    root.querySelectorAll('img').forEach(function (img) {
        const stored = img.getAttribute('data-nv-src') || img.getAttribute('src') || '';
        if (!stored || stored.indexOf('://') >= 0) return;
        img.setAttribute('data-nv-src', stored);
        img.setAttribute('src', window.NovalistEditorState.imageBaseUrl + stored.split('/').map(encodeURIComponent).join('/'));
    });
}

/**
 * Inserts an image at the caret, on a line of its own. The alt text is what a
 * reader who cannot see it gets, and it travels into every export.
 */
function insertImageAtCaret(storedPath, alt) {
    const paragraph = document.createElement('p');
    paragraph.className = 'nv-image';
    const img = document.createElement('img');
    img.setAttribute('data-nv-src', storedPath);
    img.setAttribute('alt', alt || '');
    img.setAttribute('src', window.NovalistEditorState.imageBaseUrl + storedPath.split('/').map(encodeURIComponent).join('/'));
    paragraph.appendChild(img);

    // The remembered paragraph, unless the scene has been rebuilt under it -
    // switching scene mid-dialog detaches it, and putting the picture into the
    // scene that happens to be open now is worse than putting it at the end.
    const remembered = window.NovalistEditorState.imageTargetBlock;
    window.NovalistEditorState.imageTargetBlock = null;
    const block = remembered && window.NovalistEditorState.editor.contains(remembered) ? remembered : window.caretBlock();
    // Into whatever holds the paragraph, which in page view is the .nv-page
    // rather than the editor: testing for the editor put every image in page
    // view at the end of the scene, and outside the paper surface at that.
    if (block && block.parentNode && window.NovalistEditorState.editor.contains(block)) {
        block.parentNode.insertBefore(paragraph, block.nextSibling);
    } else {
        window.NovalistEditorState.editor.appendChild(paragraph);
    }
    // Inserting is a content change the input listener never sees, because
    // nothing was typed.
    window.NovalistEditorState.editor.dispatchEvent(new Event('input'));
}

function getCleanContentHtml() {
    const clone = window.NovalistEditorState.editor.cloneNode(true);
    if (window.NovalistEditorState.pageViewEnabled) {
        clone.querySelectorAll('.nv-page').forEach(p => {
            const parent = p.parentNode;
            while (p.firstChild) parent.insertBefore(p.firstChild, p);
            parent.removeChild(p);
        });
    }
    // Images go back to the path the project stores. The display URL points at
    // this machine's copy of the project and would be meaningless in anyone
    // else's, including this writer's after a move.
    clone.querySelectorAll('img[data-nv-src]').forEach(function (img) {
        img.setAttribute('src', img.getAttribute('data-nv-src'));
        img.removeAttribute('data-nv-src');
    });
    return clone.innerHTML;
}

function reportContentChanged() {
    if (window.NovalistEditorState.isComposing) return;
    window.refreshSceneBreakClasses(window.NovalistEditorState.editor);
    const html = getCleanContentHtml();
    if (html !== window.NovalistEditorState.lastHtml) {
        window.NovalistEditorState.lastHtml = html;
        window.sendMessage({
            type: 'contentChanged',
            html: html,
            plainText: window.NovalistEditorState.editor.innerText || ''
        });
    }
}

/** Flushes the iframe's short debounce before an application-level save. */
function flushPendingContentChange() {
    if (window.NovalistEditorState.contentChangeTimer) clearTimeout(window.NovalistEditorState.contentChangeTimer);
    window.NovalistEditorState.contentChangeTimer = null;
    if (!window.NovalistEditorState.isSettingContent) reportContentChanged();
}

// Chromium owns a live text range until composition ends. Moving paragraphs,
// wrapping grammar spans or replacing text during that interval commits the
// unfinished pinyin, leaving it beside the Chinese text chosen afterwards.
function isCompositionInput(event) {
    return window.NovalistEditorState.isComposing || !!(event && (event.isComposing || event.keyCode === 229));
}

function handleEditorInput(event) {
    if (window.NovalistEditorState.isSettingContent || isCompositionInput(event)) return;
    if (event && event.inputType === 'insertParagraph') {
        // Chromium clones inline spans into the empty paragraph after Enter.
        // A comment belongs to its words, not to the new paragraph. Keep the
        // nodes (and any bold/italic children) so native undo/redo and the caret
        // still point to the browser's own nodes.
        window.NovalistEditorState.editor.querySelectorAll('span.nv-comment[data-comment-id]').forEach(span => {
            if (!span.textContent) window.clearCommentMark(span);
        });
    }
    window.scheduleTypewriterRecenter();
    // Typing over a passage being read back is the writer taking over.
    if (window.NovalistEditorState.readAloudActive) window.stopReadAloud();
    window.requestReadability();

    // Debounced dialogue correction — runs after typing pauses
    if (window.NovalistEditorState.dialogueCorrectionConfig) {
        if (window.NovalistEditorState.dialogueCorrectionTimer) clearTimeout(window.NovalistEditorState.dialogueCorrectionTimer);
        window.NovalistEditorState.dialogueCorrectionTimer = setTimeout(() => {
            window.NovalistEditorState.dialogueCorrectionTimer = null;
            window.tryDialogueCorrection();
        }, 600);
    }

    if (window.NovalistEditorState.contentChangeTimer) clearTimeout(window.NovalistEditorState.contentChangeTimer);
    window.NovalistEditorState.contentChangeTimer = setTimeout(() => {
        window.NovalistEditorState.contentChangeTimer = null;
        reportContentChanged();
    }, 50);

    // Request grammar check after typing pause
    window.requestGrammarCheck();
}

// Capture-phase keydown for mention picker navigation — runs before main keydown.
function forwardEditorHotkey(e) {
    window.sendMessage({
            type: 'hotkey',
            key: e.key,
            code: e.code,
            ctrlKey: e.ctrlKey,
            metaKey: e.metaKey,
            shiftKey: e.shiftKey,
            altKey: e.altKey
        });
}

Object.assign(window, {
    setImageBase,
    toDisplayImages,
    insertImageAtCaret,
    getCleanContentHtml,
    reportContentChanged,
    flushPendingContentChange,
    isCompositionInput,
    handleEditorInput,
    forwardEditorHotkey
});
