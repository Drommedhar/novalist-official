/* DOMPurify is loaded locally before this script in every prose frame. */
const sceneHtmlDocument = document.implementation.createHTMLDocument('');
const sceneHtmlForm = sceneHtmlDocument.createElement('form');
const sceneHtmlWindowNames = new Set();
for (let owner = window; owner; owner = Object.getPrototypeOf(owner)) {
    for (const name of Object.getOwnPropertyNames(owner)) sceneHtmlWindowNames.add(name);
}

// Prose anchors such as "target" also name form properties. Forms cannot enter
// these frames, so preserve only those IDs while retaining DOMPurify's document
// clobbering protection and all name-attribute checks. The pristine objects and
// window snapshot keep a second pass from rejecting IDs inserted by the first.
window.DOMPurify.addHook('uponSanitizeAttribute', (node, attribute, config) => {
    if (node.namespaceURI === 'http://www.w3.org/1999/xhtml'
        && attribute.attrName === 'id'
        && attribute.attrValue in sceneHtmlForm
        && !(attribute.attrValue in sceneHtmlDocument)
        && !sceneHtmlWindowNames.has(attribute.attrValue)
        && !config.FORBID_ATTR?.includes('id')
        && config.FORBID_TAGS?.includes('form')) {
        attribute.forceKeepAttr = true;
    }
});

function sceneSanitizerOptions() {
    return {
        USE_PROFILES: { html: true },
        // Preserve formatting and Novalist's data-* annotations, while keeping
        // project content from adding controls or document-wide styles.
        ADD_ATTR: ['contenteditable'],
        FORBID_TAGS: ['style', 'form', 'input', 'button', 'textarea', 'select', 'option'],
        // DOMPurify's default safe schemes plus the app's read-only resources.
        // Unknown protocols remain blocked, including javascript: and data:
        // links (DOMPurify separately permits data images on image elements).
        ALLOWED_URI_REGEXP: /^(?:(?:(?:f|ht)tps?|mailto|tel|callto|sms|cid|xmpp|matrix|novalist|novalist-project|novalist-asset|novalist-audio):|[^a-z]|[a-z+.-]+(?:[^a-z+.\-:]|$))/i
    };
}

function sanitizeSceneHtml(html) {
    return window.DOMPurify.sanitize(html || '', sceneSanitizerOptions());
}

function sanitizeSceneFragment(html) {
    return window.DOMPurify.sanitize(html || '', {
        ...sceneSanitizerOptions(),
        RETURN_DOM_FRAGMENT: true
    });
}

Object.assign(window, { sanitizeSceneHtml, sanitizeSceneFragment });

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
