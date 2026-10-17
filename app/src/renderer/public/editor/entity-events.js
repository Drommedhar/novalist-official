'use strict';

function leaveEntity() {
    const state = window.NovalistEditorState;
    if (!state.lastHoveredAlias && !state.lastHoveredMentionId) return;
    state.lastHoveredAlias = null;
    state.lastHoveredMentionId = null;
    window.sendMessage({ type: 'entityExit' });
}

window.NovalistEditorState.editor.addEventListener('mousemove', (event) => {
    const state = window.NovalistEditorState;
    const mention = event.target.closest?.('.nv-entity-mention');
    const id = mention?.getAttribute('data-entity-id');
    if (id) {
        if (state.lastHoveredMentionId === id) return;
        state.lastHoveredMentionId = id;
        state.lastHoveredAlias = null;
        const rect = mention.getBoundingClientRect();
        window.sendMessage({
            type: 'entityMentionHover', entityId: id,
            rect: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom }
        });
        return;
    }
    const hit = state.entityRegex && window.findEntityAtPoint(event.clientX, event.clientY);
    if (!hit) { leaveEntity(); return; }
    if (state.lastHoveredAlias === hit.alias) return;
    state.lastHoveredMentionId = null;
    state.lastHoveredAlias = hit.alias;
    window.sendMessage({ type: 'entityHover', alias: hit.alias, rect: hit.rect });
});

// Cancel pending hover immediately; the host owns dismissal debounce.
window.NovalistEditorState.editor.addEventListener('mouseleave', leaveEntity);
window.NovalistEditorState.editor.addEventListener('mousedown', () => {
    leaveEntity();
    window.sendMessage({ type: 'pointerPressed' });
});

window.NovalistEditorState.editor.addEventListener('keydown', (e) => {
    if (window.isCompositionInput(e)) return;
    // Forward function keys (F1-F12) and Esc unconditionally so host hotkeys
    // like F11 (focus mode) and Esc work even while focus is in the editor.
    var isFunctionKey = /^F\d{1,2}$/.test(e.code);
    if (isFunctionKey || e.key === 'Escape') {
        e.preventDefault();
        window.forwardEditorHotkey(e);
        return;
    }

    // Forward modifier key combinations to the host app for hotkey dispatch.
    // Exclude standard text-editing shortcuts handled natively by contenteditable.
    // metaKey counts: the host treats Cmd as Ctrl, and on an iPad with a hardware
    // keyboard (or macOS with the menu unavailable) Cmd is the only modifier a
    // writer actually presses - without it every Cmd shortcut died in the editor.
    if ((e.ctrlKey || e.metaKey || e.altKey) && !window.isTextEditingShortcut(e)) {
        e.preventDefault();
        window.forwardEditorHotkey(e);
    }
});
