'use strict';

/** Re-selects the range the menu was opened on. False when there was none. */
function restoreContextMenuSelection() {
    if (!window.NovalistEditorState.contextMenuRange) {
        // No captured range means the action was run from the command palette
        // or a gesture rather than from the menu, and the live selection is the
        // one the writer means.
        const live = window.getSelection();
        return !!(live && live.rangeCount > 0);
    }
    const sel = window.getSelection();
    if (!sel) return false;
    sel.removeAllRanges();
    sel.addRange(window.NovalistEditorState.contextMenuRange);
    return true;
}

/** The passage an action works on: what the menu captured, else what is selected. */
function contextActionText() {
    if (window.NovalistEditorState.contextMenuText) return window.NovalistEditorState.contextMenuText;
    const sel = window.getSelection();
    return sel ? sel.toString() : '';
}

function setContextMenuLabels(labelsJson) {
    try {
        window.NovalistEditorState.contextMenuLabels = JSON.parse(labelsJson);
        const floatingLabels = {
            'ft-bold': window.NovalistEditorState.contextMenuLabels.bold,
            'ft-italic': window.NovalistEditorState.contextMenuLabels.italic,
            'ft-underline': window.NovalistEditorState.contextMenuLabels.underline,
            'ft-strike': window.NovalistEditorState.contextMenuLabels.strikethrough,
            'ft-highlight': window.NovalistEditorState.contextMenuLabels.highlight,
            'ft-link': window.NovalistEditorState.contextMenuLabels.link,
            'ft-comment': window.NovalistEditorState.contextMenuLabels.addComment,
            'ft-footnote': window.NovalistEditorState.contextMenuLabels.addFootnote
        };
        for (const id in floatingLabels) {
            const button = document.getElementById(id);
            const label = floatingLabels[id];
            if (!button || !label) continue;
            button.title = label;
            button.setAttribute('aria-label', label);
            if (id === 'ft-comment' || id === 'ft-footnote' || id === 'ft-link') {
                button.textContent = label;
            }
        }
    } catch (error) { console.warn("editor: setContextMenuLabels failed", error instanceof Error ? error.name : typeof error); }
}

/**
 * One group of the context menu, drawn as a flyout.
 *
 * Rows that need a selection are disabled rather than hidden, so the menu keeps
 * the same shape whether or not anything is selected - a menu that reshuffles
 * itself is a menu nobody learns.
 *
 * A group in which every row is disabled is dropped entirely, though. Keeping
 * it leaves a flyout that opens onto nothing usable, which reads as broken
 * rather than as unavailable - the group name gives no hint that a selection
 * is what it wants.
 */
function submenu(title, rows, hasSelection) {
    if (!rows || rows.length === 0) return '';
    const usable = rows.some((row) => !row.needsSelection || hasSelection);
    if (!usable) return '';
    let inner = '';
    for (const row of rows) {
        const off = row.needsSelection && !hasSelection ? ' disabled' : '';
        const attr = row.inlineAction
            ? ' data-inline-action="' + window.escapeAttr(row.inlineAction) + '"'
            : ' data-action="' + window.escapeAttr(row.action) + '"'
                // The registry id this row is the rendering of, so the
                // placement doctor can see where a command actually lives.
                + (row.command ? ' data-command="' + window.escapeAttr(row.command) + '"' : '');
        inner += '<div class="cm-item' + off + '"' + attr + '><span>'
            + escapeHtml(row.label) + '</span></div>';
    }
    return '<div class="cm-parent"><div class="cm-item"><span>'
        + escapeHtml(title) + '</span></div>'
        + '<div class="cm-submenu">' + inner + '</div></div>';
}

/**
 * One of the editor's own actions on the passage in front of the writer.
 *
 * Extracted from the context menu's click handler, where every one of these
 * bodies used to sit inline - which made right-clicking the only way to reach
 * them. Named, they are also commands: the palette can offer them and a writer
 * can bind a gesture to any of them.
 */
function applyContextAction(action) {
    switch (action) {
        case 'cut':
            document.execCommand('cut');
            break;
        case 'copy':
            document.execCommand('copy');
            break;
        case 'paste':
            navigator.clipboard.readText().then(text => {
                document.execCommand('insertText', false, text);
            }).catch(() => {
                document.execCommand('paste');
            });
            break;
        case 'selectAll':
            document.execCommand('selectAll');
            break;
        case 'peekEntity':
            restoreContextMenuSelection();
            window.peekEntityAtCaret();
            break;
        case 'insertImage': {
            // The frame owns no file picker; the host asks and answers with a
            // stored path, which insertImageAtCaret then places. Where it goes
            // has to be settled now: the host's dialogs take focus, and the
            // caret does not survive them.
            restoreContextMenuSelection();
            const target = window.NovalistEditorState.contextMenuImageTarget;
            if (target) window.NovalistEditorState.imageTargetBlock = target;
            else window.rememberImageTarget();
            window.sendMessage({ type: 'insertImageRequested' });
            break;
        }
        case 'splitAtCaret': {
            // Acts on the caret captured when the menu opened, since opening it
            // moved focus out of the editor.
            restoreContextMenuSelection();
            const halves = window.splitAtCaret();
            if (!halves) break;
            const parsed = JSON.parse(halves);
            window.sendMessage({ type: 'splitSceneRequested', before: parsed.before, after: parsed.after });
            break;
        }
        case 'cutToDarlings': {
            // The prose leaves the scene and lands in the bin in one action.
            // Cutting first and asking the writer to file it afterwards is how
            // the paragraph gets lost between the two.
            if (!restoreContextMenuSelection()) break;
            const kept = contextActionText();
            if (!kept || kept.trim().length === 0) break;
            window.sendMessage({ type: 'keepDarling', text: kept });
            document.execCommand('delete');
            // execCommand fires input on its own, but the deletion is worth
            // being explicit about: the prose has left the scene.
            window.NovalistEditorState.editor.dispatchEvent(new Event('input'));
            break;
        }
        case 'auditionLine': {
            // The host looks the line up in the scene rather than speaking the
            // selection as raw text, so it arrives in the voice of whoever says
            // it and directed the way the reading would have it.
            const line = contextActionText();
            if (!line || line.trim().length === 0) break;
            window.sendMessage({ type: 'auditionLine', text: line });
            break;
        }
        case 'createEntityFromSelection': {
            createEntityFromContextSelection();
            break;
        }
        case 'appendToEntitySection': {
            // Copies the passage into a Codex section; the prose is left untouched.
            const text = contextActionText().trim();
            if (text.length === 0) break;
            window.sendMessage({ type: 'appendToEntityRequested', text: text });
            break;
        }
    }
}

function createEntityFromContextSelection() {
    if (!restoreContextMenuSelection()) return;
    const name = contextActionText().trim();
    if (name.length === 0) return;
    const pendingId = 'pm-' + (++window.NovalistEditorState.mentionPendingSeq) + '-' + Date.now();
    const html = '<span class="nv-mention-pending" data-pending-id="'
        + window.escapeAttr(pendingId) + '">' + escapeHtml(name) + '</span>';
    let inserted = false;
    try { inserted = document.execCommand('insertHTML', false, html); }
    catch (error) { console.warn('Entity mention insertion failed', error instanceof Error ? error.name : typeof error); }
    if (!inserted) return;
    window.queueContentChangedSoon();
    window.sendMessage({ type: 'mentionCreateRequested', name, pendingId });
}

function hideContextMenu() {
    window.NovalistEditorState.contextMenu.classList.remove('visible');
    // Drop the captured selection so a later action can never act on a stale range.
    window.NovalistEditorState.contextMenuRange = null;
    window.NovalistEditorState.contextMenuImageTarget = null;
    window.NovalistEditorState.contextMenuText = '';
    window.NovalistEditorState.contextSpellingPoint = null;
    window.NovalistEditorState.contextSpellingTarget = null;
    // The selection usually survives the menu, so the format bar is due back.
    window.updateFloatingToolbar();
}

function escapeHtml(str) {
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

Object.assign(window, {
    restoreContextMenuSelection,
    contextActionText,
    setContextMenuLabels,
    submenu,
    applyContextAction,
    createEntityFromContextSelection,
    hideContextMenu,
    escapeHtml
});
