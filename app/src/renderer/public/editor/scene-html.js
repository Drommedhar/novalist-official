const { sanitizeSceneHtml, sanitizeSceneFragment } = NovalistSceneSanitizer.createSceneSanitizer(window, window.DOMPurify);
window.sanitizeSceneHtml = sanitizeSceneHtml;
window.sanitizeSceneFragment = sanitizeSceneFragment;

function sceneContentFragment(html) {
    const fragment = sanitizeSceneFragment(html);
    // Scene files may be formatted with newlines between HTML blocks. The
    // editors use pre-wrap for prose, which would paint each of those source
    // separators as an extra blank line. Only remove whitespace bounded by
    // blocks (or the container edge); inline spaces, verse, plain text and
    // explicit empty paragraphs remain author content.
    const blocks = new Set([
        'ADDRESS', 'ARTICLE', 'ASIDE', 'BLOCKQUOTE', 'DIV', 'DL', 'DT', 'DD',
        'FIGURE', 'FIGCAPTION', 'FOOTER', 'HEADER', 'H1', 'H2', 'H3', 'H4',
        'H5', 'H6', 'HR', 'LI', 'MAIN', 'NAV', 'OL', 'P', 'PRE', 'SECTION',
        'TABLE', 'THEAD', 'TBODY', 'TFOOT', 'TR', 'TD', 'TH', 'UL'
    ]);
    const walker = document.createTreeWalker(fragment, NodeFilter.SHOW_TEXT);
    const separators = [];
    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
        if (!/^[\t\r\n ]+$/.test(node.data) || node.parentElement?.closest('pre, .nv-style-poetry')) continue;
        const before = node.previousSibling;
        const after = node.nextSibling;
        const beforeBlock = before && blocks.has(before.nodeName);
        const afterBlock = after && blocks.has(after.nodeName);
        if ((!before || beforeBlock) && (!after || afterBlock) && (beforeBlock || afterBlock)) {
            separators.push(node);
        }
    }
    separators.forEach(node => node.remove());
    return fragment;
}

window.sceneContentFragment = sceneContentFragment;

window.applyFrameTheme = function (colors, properties) {
    properties.forEach((property, index) => {
        if (index < 2 || colors[index]) {
            document.documentElement.style.setProperty(property, colors[index]);
        }
    });
};

// Native rich-text drops bypass the paste handler. Keep in-editor moves native
// (including their undo history), but sanitize HTML arriving from other pages.
let sceneHtmlInternalDrag = false;
document.addEventListener('dragstart', () => { sceneHtmlInternalDrag = true; });
document.addEventListener('dragend', () => { sceneHtmlInternalDrag = false; });
document.addEventListener('drop', (event) => {
    if (sceneHtmlInternalDrag) return;
    const html = event.dataTransfer?.getData('text/html');
    const target = event.target instanceof Element
        ? event.target.closest('[contenteditable="true"]') : null;
    if (!html || !target) return;
    event.preventDefault();
    const range = document.caretRangeFromPoint(event.clientX, event.clientY);
    if (!range || !target.contains(range.startContainer)) return;
    const selection = window.getSelection();
    if (!selection) return;
    target.focus({ preventScroll: true });
    selection.removeAllRanges();
    selection.addRange(range);
    document.execCommand('insertHTML', false, sanitizeSceneHtml(html));
});
