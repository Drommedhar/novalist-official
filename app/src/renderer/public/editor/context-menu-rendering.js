'use strict';

function grammarContextMenuHtml(e) {
    let html = '';
    // Check if right-clicking on a grammar issue
    const issueSpan = e.target.closest('.grammar-issue');
    if (issueSpan && window.NovalistEditorState.grammarEnabled) {
        const index = parseInt(issueSpan.getAttribute('data-issue-index'), 10);
        const issue = window.NovalistEditorState.grammarIssues[index];
        if (issue) {
            html += '<div class="cm-grammar-msg">' + window.escapeHtml(issue.message) + '</div>';
            if (issue.replacements && issue.replacements.length > 0) {
                for (const rep of issue.replacements) {
                    html += '<div class="cm-suggestion" data-issue-index="' + index + '" data-replacement="' + window.escapeHtml(rep) + '">' + window.escapeHtml(rep || window.NovalistEditorState.contextMenuLabels.removeText || 'Delete') + '</div>';
                }
            }
            if (issue.type === 'spelling') {
                const word = issue.text || '';
                if (word.trim().length > 0) {
                    html += '<div class="cm-item" data-action="addToDictionary" data-word="' + window.escapeHtml(word.trim()) + '"><span>' + window.escapeHtml(window.NovalistEditorState.contextMenuLabels.addToDictionary || 'Add to Dictionary') + '</span></div>';
                }
            }
            html += '<div class="cm-separator"></div>';
        }
    }
    return html;
}

function editingContextMenuHtml(hasSelection) {
    let html = '';
    // Standard editing items
    const disabledAttr = hasSelection ? '' : ' disabled';
    html += '<div class="cm-item' + disabledAttr + '" data-action="cut"><span>' + window.escapeHtml(window.NovalistEditorState.contextMenuLabels.cut || 'Cut') + '</span><span class="cm-shortcut">Ctrl+X</span></div>';
    html += '<div class="cm-item' + disabledAttr + '" data-action="copy"><span>' + window.escapeHtml(window.NovalistEditorState.contextMenuLabels.copy || 'Copy') + '</span><span class="cm-shortcut">Ctrl+C</span></div>';
    html += '<div class="cm-item" data-action="paste"><span>' + window.escapeHtml(window.NovalistEditorState.contextMenuLabels.paste || 'Paste') + '</span><span class="cm-shortcut">Ctrl+V</span></div>';
    html += '<div class="cm-separator"></div>';
    html += '<div class="cm-item" data-action="selectAll"><span>' + window.escapeHtml(window.NovalistEditorState.contextMenuLabels.selectAll || 'Select All') + '</span><span class="cm-shortcut">Ctrl+A</span></div>';
    return html;
}

function entityContextMenuHtml(hasSelection) {
    let html = '';
    // Comment and footnote used to sit here as well as in the floating toolbar
    // and on the editor toolbar - three homes for one command. They act on a
    // selection, so the toolbar that appears over a selection is the one they
    // kept.
    //
    // What the menu gains instead is the command that genuinely belongs to the
    // thing under the pointer: the entry for the name you just right-clicked.
    // It was a button on the editor toolbar, where it appeared and disappeared
    // as the caret moved past names.
    // placement-container: contextMenu
    if (window.findEntityAtCaret()) {
        html += '<div class="cm-separator"></div>';
        html += '<div class="cm-item" data-command="caret.peekEntity" data-action="peekEntity"><span>' + window.escapeHtml(window.NovalistEditorState.contextMenuLabels.peekEntity || 'Peek at entity under caret') + '</span><span class="cm-shortcut">Ctrl+Shift+E</span></div>';
    }

    // Everything below is grouped: these belong to families and go behind
    // their names rather than lengthening the top level.
    html += '<div class="cm-separator"></div>';
    html += window.submenu(window.NovalistEditorState.contextMenuLabels.groupScene || 'Scene', [
        { action: 'splitAtCaret', command: 'caret.splitScene', label: window.NovalistEditorState.contextMenuLabels.splitScene || 'Split scene here' },
        { action: 'insertImage', command: 'caret.insertImage', label: window.NovalistEditorState.contextMenuLabels.insertImage || 'Insert image' },
        { action: 'cutToDarlings', command: 'text.cutToDarlings', label: window.NovalistEditorState.contextMenuLabels.cutToDarlings || 'Cut and keep', needsSelection: true },
        // The question in the middle of writing a line is whether it sounds
        // right in the mouth of the person saying it, and going to another view
        // to find out answers it too late to be any use.
        { action: 'auditionLine', command: 'text.auditionLine', label: window.NovalistEditorState.contextMenuLabels.auditionLine || 'Hear this line', needsSelection: true }
    ], hasSelection);
    html += window.submenu(window.NovalistEditorState.contextMenuLabels.groupCodex || 'Codex', [
        { action: 'createEntityFromSelection', command: 'text.createEntity', label: window.NovalistEditorState.contextMenuLabels.createEntity || 'Create entity from selection', needsSelection: true },
        { action: 'appendToEntitySection', command: 'text.appendToEntity', label: window.NovalistEditorState.contextMenuLabels.appendToEntity || 'Add selection to entity', needsSelection: true }
    ], hasSelection);
    // placement-container: end
    return html;
}

function extensionContextMenuHtml(hasSelection) {
    let html = '';
    console.log('[InlineActions] context menu open. hasSelection=', hasSelection, 'inlineActions.length=', (window.NovalistEditorState.inlineActions || []).length);
    // Inline actions (extension-contributed). Shown only when selection non-empty.
    const offerableActions = (window.NovalistEditorState.inlineActions || []).filter(function (a) {
        return hasSelection || a.allowsEmptySelection;
    });
    if (offerableActions.length > 0) {
        // Group the offerable ones, not every contributed action: grouping the
        // full list put actions that need a selection into the menu with no
        // selection and nothing marking them dead, so they looked clickable
        // and did nothing.
        const grouped = {};
        for (const a of offerableActions) {
            const g = a.group || '';
            (grouped[g] = grouped[g] || []).push(a);
        }
        html += '<div class="cm-separator"></div>';
        for (const groupName of Object.keys(grouped)) {
            const rows = grouped[groupName].map(function (a) {
                return {
                    inlineAction: a.id,
                    label: (a.icon ? a.icon + ' ' : '') + a.label
                };
            });
            // A named group becomes a submenu; the unnamed ones stay inline,
            // because a flyout called nothing is a flyout nobody opens.
            if (groupName) {
                html += window.submenu(groupName, rows, hasSelection);
            } else {
                for (const row of rows) {
                    html += '<div class="cm-item" data-inline-action="' + window.escapeAttr(row.inlineAction) + '"><span>' + window.escapeHtml(row.label) + '</span></div>';
                }
            }
        }
    }

    // Extension context-menu items (always shown; operate on the current scene).
    if (window.NovalistEditorState.extensionMenuItems && window.NovalistEditorState.extensionMenuItems.length > 0) {
        html += '<div class="cm-separator"></div>';
        for (const it of window.NovalistEditorState.extensionMenuItems) {
            const icon = it.icon ? (window.escapeHtml(it.icon) + ' ') : '';
            html += '<div class="cm-item" data-ext-ctx="' + window.escapeHtml(it.id) + '"><span>' + icon + window.escapeHtml(it.label) + '</span></div>';
        }
    }
    return html;
}

function positionContextMenu(e, contextMenu) {
    // Position
    let x = e.clientX, y = e.clientY;
    contextMenu.classList.add('visible');
    const rect = contextMenu.getBoundingClientRect();
    if (x + rect.width > window.innerWidth) x = window.innerWidth - rect.width - 4;
    if (y + rect.height > window.innerHeight) y = window.innerHeight - rect.height - 4;
    contextMenu.style.left = x + 'px';
    contextMenu.style.top = y + 'px';

    // A flyout opens rightwards unless there is no room, which there never is
    // when the menu was raised near the right edge - and a submenu the writer
    // cannot see is a group they cannot reach.
    const room = window.innerWidth - (x + rect.width);
    for (const parent of contextMenu.querySelectorAll('.cm-parent')) {
        parent.classList.toggle('flip', room < 210);
        parent.addEventListener('mouseenter', () => {
            const submenu = parent.querySelector('.cm-submenu');
            submenu.style.top = '';
            const flyout = submenu.getBoundingClientRect();
            const top = Math.max(4, Math.min(flyout.top, window.innerHeight - flyout.height - 4));
            submenu.style.top = (top - parent.getBoundingClientRect().top) + 'px';
        });
    }
}

Object.assign(window, {
    grammarContextMenuHtml,
    editingContextMenuHtml,
    entityContextMenuHtml,
    extensionContextMenuHtml,
    positionContextMenu
});
