const { sanitizeSceneHtml, sanitizeSceneFragment } = NovalistSceneSanitizer.createSceneSanitizer(window, window.DOMPurify);
window.sanitizeSceneHtml = sanitizeSceneHtml;
window.sanitizeSceneFragment = sanitizeSceneFragment;

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
