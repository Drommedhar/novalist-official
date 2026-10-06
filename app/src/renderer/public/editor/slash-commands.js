'use strict';

function slashActions() {
    return (window.NovalistEditorState.inlineActions || []).filter(function (a) { return a.allowsEmptySelection; });
}

/** The keyword typed after the slash; falls back to the id's last segment. */
function slashKeywordOf(action) {
    if (action.slashKeyword) return action.slashKeyword;
    const dot = action.id.lastIndexOf('.');
    return dot >= 0 ? action.id.slice(dot + 1) : action.id;
}

/**
 * A slash that starts a command rather than one inside a word or a date. Only
 * at the very start of a line, so "and/or" and "24/7" are left alone.
 */
function detectSlashAtCaret() {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0 || !sel.isCollapsed) return null;
    const range = sel.getRangeAt(0);
    if (range.startContainer.nodeType !== Node.TEXT_NODE) return null;
    const text = range.startContainer.textContent || '';
    const offset = range.startOffset;
    if (offset === 0) return null;

    // Walk back to the slash that opened this command, stopping at anything
    // that means it was not a command after all.
    let at = -1;
    for (let i = offset - 1; i >= 0; i--) {
        const ch = text.charAt(i);
        if (ch === '/') { at = i; break; }
        if (ch === '\n') return null;
    }
    if (at < 0) return null;
    // Everything before the slash on this line has to be blank.
    if (text.slice(0, at).trim().length > 0) return null;
    return { node: range.startContainer, atOffset: at, query: text.slice(at + 1, offset) };
}

function openSlashMenu() {
    window.NovalistEditorState.slashState = detectSlashAtCaret();
    if (!window.NovalistEditorState.slashState || slashActions().length === 0) { window.NovalistEditorState.slashState = null; return; }
    window.NovalistEditorState.slashSelectedIndex = 0;
    renderSlashMenu();
}

function closeSlashMenu() {
    window.NovalistEditorState.slashState = null;
    window.NovalistEditorState.slashFiltered = [];
    const picker = document.getElementById('slash-picker');
    if (picker) picker.style.display = 'none';
}

function updateSlashQuery() {
    const found = detectSlashAtCaret();
    if (!found) { closeSlashMenu(); return; }
    window.NovalistEditorState.slashState = found;
    renderSlashMenu();
}

function renderSlashMenu() {
    let picker = document.getElementById('slash-picker');
    if (!picker) {
        picker = document.createElement('div');
        picker.id = 'slash-picker';
        picker.className = 'mention-picker';
        document.body.appendChild(picker);
    }

    // The query is everything after the slash, but only its first word selects
    // the command - the rest is the directive the action writes towards.
    const raw = window.NovalistEditorState.slashState.query || '';
    const space = raw.indexOf(' ');
    const keyword = (space < 0 ? raw : raw.slice(0, space)).toLowerCase();

    window.NovalistEditorState.slashFiltered = slashActions().filter(function (a) {
        return keyword.length === 0 || slashKeywordOf(a).toLowerCase().indexOf(keyword) === 0;
    });

    if (window.NovalistEditorState.slashFiltered.length === 0) { closeSlashMenu(); return; }
    if (window.NovalistEditorState.slashSelectedIndex >= window.NovalistEditorState.slashFiltered.length) window.NovalistEditorState.slashSelectedIndex = 0;

    let html = '';
    for (let i = 0; i < window.NovalistEditorState.slashFiltered.length; i++) {
        const action = window.NovalistEditorState.slashFiltered[i];
        html += '<div class="mention-item' + (i === window.NovalistEditorState.slashSelectedIndex ? ' selected' : '') +
            '" data-slash-index="' + i + '">' +
            '<span class="mention-name">/' + slashKeywordOf(action) + '</span>' +
            '<span class="mention-detail">' + action.label + '</span></div>';
    }
    picker.replaceChildren(window.sanitizeSceneFragment(html));
    picker.style.display = 'block';

    const rect = window.getCaretRect();
    if (rect) {
        picker.style.left = rect.left + 'px';
        picker.style.top = (rect.bottom + 4) + 'px';
    }

    picker.querySelectorAll('[data-slash-index]').forEach(function (row) {
        row.addEventListener('mousedown', function (e) {
            e.preventDefault();
            confirmSlashAt(parseInt(row.getAttribute('data-slash-index'), 10));
        });
    });
}

function moveSlashSelection(delta) {
    if (window.NovalistEditorState.slashFiltered.length === 0) return;
    window.NovalistEditorState.slashSelectedIndex = (window.NovalistEditorState.slashSelectedIndex + delta + window.NovalistEditorState.slashFiltered.length) % window.NovalistEditorState.slashFiltered.length;
    renderSlashMenu();
}

/**
 * Runs the chosen command. The typed text - slash, keyword and directive - is
 * removed first, so the writer is left with the prose the action produces and
 * not with the instruction they typed to get it.
 */
function confirmSlashAt(index) {
    if (!window.NovalistEditorState.slashState || index < 0 || index >= window.NovalistEditorState.slashFiltered.length) return;
    const action = window.NovalistEditorState.slashFiltered[index];
    const raw = window.NovalistEditorState.slashState.query || '';
    const space = raw.indexOf(' ');
    const directive = space < 0 ? '' : raw.slice(space + 1).trim();

    const node = window.NovalistEditorState.slashState.node;
    const atOffset = window.NovalistEditorState.slashState.atOffset;
    closeSlashMenu();

    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return;
    const caret = sel.getRangeAt(0);
    const range = document.createRange();
    range.setStart(node, atOffset);
    range.setEnd(caret.startContainer, caret.startOffset);
    range.deleteContents();
    sel.removeAllRanges();
    sel.addRange(range);

    window.NovalistEditorState.editor.dispatchEvent(new Event('input'));
    window.triggerInlineAction(action.id, directive);
}

Object.assign(window, {
    slashActions,
    slashKeywordOf,
    detectSlashAtCaret,
    openSlashMenu,
    closeSlashMenu,
    updateSlashQuery,
    renderSlashMenu,
    moveSlashSelection,
    confirmSlashAt
});
