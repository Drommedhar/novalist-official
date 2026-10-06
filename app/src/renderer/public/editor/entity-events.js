'use strict';

window.NovalistEditorState.editor.addEventListener('mousemove', (e) => {
    // First, check if the cursor is over an `nv-entity-mention` span — preferred
    // because it carries a stable entity ID immune to renames.
    const target = e.target;
    let mention = null;
    if (target && target.closest) mention = target.closest('.nv-entity-mention');
    if (mention) {
        const id = mention.getAttribute('data-entity-id') || '';
        if (window.NovalistEditorState.entityExitTimer) { clearTimeout(window.NovalistEditorState.entityExitTimer); window.NovalistEditorState.entityExitTimer = null; }
        if (id && id !== window.NovalistEditorState.lastHoveredMentionId) {
            window.NovalistEditorState.lastHoveredMentionId = id;
            window.NovalistEditorState.lastHoveredAlias = null;
            // The span, not the pointer. An explicit mention was the one entity
            // reference still anchored to a point: the card cleared the pixel
            // under the cursor and covered the rest of the name, which is the
            // condition the whole hide/show flicker loop grows out of.
            const box = mention.getBoundingClientRect();
            window.sendMessage({
                type: 'entityMentionHover',
                entityId: id,
                x: e.clientX,
                y: e.clientY,
                rect: { left: box.left, top: box.top, right: box.right, bottom: box.bottom }
            });
        }
        return;
    }
    if (window.NovalistEditorState.lastHoveredMentionId) {
        // Left a mention span — debounce exit
        if (!window.NovalistEditorState.entityExitTimer) {
            window.NovalistEditorState.entityExitTimer = setTimeout(() => {
                window.NovalistEditorState.entityExitTimer = null;
                window.NovalistEditorState.lastHoveredMentionId = null;
                window.NovalistEditorState.lastHoveredAlias = null;
                window.sendMessage({ type: 'entityExit' });
            }, 200);
        }
        return;
    }

    if (!window.NovalistEditorState.entityRegex) return;
    const hit = window.findEntityAtPoint(e.clientX, e.clientY);
    if (hit) {
        // Found an entity — cancel any pending exit and notify if alias changed
        if (window.NovalistEditorState.entityExitTimer) { clearTimeout(window.NovalistEditorState.entityExitTimer); window.NovalistEditorState.entityExitTimer = null; }
        if (hit.alias !== window.NovalistEditorState.lastHoveredAlias) {
            window.NovalistEditorState.lastHoveredAlias = hit.alias;
            window.sendMessage({
                type: 'entityHover',
                alias: hit.alias,
                x: e.clientX,
                y: e.clientY,
                rect: hit.rect
            });
        }
    } else if (window.NovalistEditorState.lastHoveredAlias) {
        // Lost entity match — debounce the exit so small pixel jitter doesn't kill the peek
        if (!window.NovalistEditorState.entityExitTimer) {
            window.NovalistEditorState.entityExitTimer = setTimeout(() => {
                window.NovalistEditorState.entityExitTimer = null;
                window.NovalistEditorState.lastHoveredAlias = null;
                window.sendMessage({ type: 'entityExit' });
            }, 200);
        }
    }
});

window.NovalistEditorState.editor.addEventListener('mouseleave', () => {
    if (window.NovalistEditorState.entityExitTimer) { clearTimeout(window.NovalistEditorState.entityExitTimer); window.NovalistEditorState.entityExitTimer = null; }
    if (window.NovalistEditorState.lastHoveredAlias || window.NovalistEditorState.lastHoveredMentionId) {
        window.NovalistEditorState.lastHoveredAlias = null;
        window.NovalistEditorState.lastHoveredMentionId = null;
        window.sendMessage({ type: 'entityExit' });
    }
});

window.NovalistEditorState.editor.addEventListener('mousedown', () => {
    window.sendMessage({ type: 'pointerPressed' });
    // A click hides the card; allow the next pointer move to reopen it.
    if (window.NovalistEditorState.entityExitTimer) { clearTimeout(window.NovalistEditorState.entityExitTimer); window.NovalistEditorState.entityExitTimer = null; }
    window.NovalistEditorState.lastHoveredMentionId = null;
    window.NovalistEditorState.lastHoveredAlias = null;
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
