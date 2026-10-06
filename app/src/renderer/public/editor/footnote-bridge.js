'use strict';

/** The footnote markers in the scene, in the order they are read. */
window.footnoteOrder = function () {
    return Array.from(window.NovalistEditorState.editor.querySelectorAll('sup.nv-fn[data-fn-id]'))
        .map(function (s) { return s.getAttribute('data-fn-id'); });
};

window.insertFootnoteAtSelection = function (footnoteId) {
    var sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return null;
    var range = sel.getRangeAt(0);
    var sup = document.createElement('sup');
    sup.className = 'nv-fn';
    sup.setAttribute('data-fn-id', footnoteId);
    sup.textContent = '0';
    range.collapse(false);
    range.insertNode(sup);
    range.setStartAfter(sup);
    range.setEndAfter(sup);
    sel.removeAllRanges();
    sel.addRange(range);
    var ids = window.renumberFootnotes();
    var num = ids.indexOf(footnoteId) + 1;
    try { window.NovalistEditorState.editor.dispatchEvent(new Event('input', { bubbles: true })); } catch (error) { console.warn("editor: operation failed", error instanceof Error ? error.name : typeof error); }
    window.sendMessage({ type: 'footnoteInserted', footnoteId: footnoteId, number: num, ids: ids });
    return num;
};

window.setFootnotesData = function (data) {
    try {
        var arr = typeof data === 'string' ? JSON.parse(data) : (data || []);
        var byId = {};
        for (var i = 0; i < arr.length; i++) byId[arr[i].id] = arr[i].text || '';
        var sups = window.NovalistEditorState.editor.querySelectorAll('sup.nv-fn[data-fn-id]');
        sups.forEach(function (s) {
            var id = s.getAttribute('data-fn-id');
            var t = byId[id];
            if (t) s.setAttribute('title', t); else s.removeAttribute('title');
        });
    } catch (error) { console.warn("editor: operation failed", error instanceof Error ? error.name : typeof error); }
};
