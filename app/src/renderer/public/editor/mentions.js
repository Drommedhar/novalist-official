'use strict';

function setMentionCandidates(candidatesJson) {
    try { window.NovalistEditorState.mentionCandidates = JSON.parse(candidatesJson) || []; }
    catch { window.NovalistEditorState.mentionCandidates = []; }
}

function isWordChar(ch) {
    return /[\p{L}\p{N}_]/u.test(ch || '');
}

function detectMentionAtCaret() {
    // Returns {node, atOffset} if caret is right after a `@` that started a
    // mention sequence (i.e. preceded by start-of-text or a non-word char).
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0 || !sel.isCollapsed) return null;
    const range = sel.getRangeAt(0);
    if (range.startContainer.nodeType !== Node.TEXT_NODE) return null;
    const text = range.startContainer.textContent || '';
    const offset = range.startOffset;
    if (offset === 0) return null;
    if (text.charAt(offset - 1) !== '@') return null;
    if (offset >= 2 && isWordChar(text.charAt(offset - 2))) return null;
    return { node: range.startContainer, atOffset: offset - 1 };
}

function openMentionPicker() {
    const pos = detectMentionAtCaret();
    if (!pos) return false;
    window.NovalistEditorState.mentionState = { node: pos.node, atOffset: pos.atOffset, query: '' };
    renderMentionPicker();
    return true;
}

function closeMentionPicker(_keepLiteral) {
    window.NovalistEditorState.mentionState = null;
    window.NovalistEditorState.mentionCreateVisible = false;
    window.NovalistEditorState.mentionPicker.classList.remove('visible');
    window.NovalistEditorState.mentionPicker.innerHTML = '';
}

function updateMentionQuery() {
    if (!window.NovalistEditorState.mentionState) return;
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0 || !sel.isCollapsed) {
        closeMentionPicker(true);
        return;
    }
    const range = sel.getRangeAt(0);
    if (range.startContainer !== window.NovalistEditorState.mentionState.node) {
        // Caret left the @-bearing text node — close.
        closeMentionPicker(true);
        return;
    }
    const txt = window.NovalistEditorState.mentionState.node.textContent || '';
    // Verify @ is still there at atOffset.
    if (window.NovalistEditorState.mentionState.atOffset >= txt.length || txt.charAt(window.NovalistEditorState.mentionState.atOffset) !== '@') {
        closeMentionPicker(true);
        return;
    }
    const caret = range.startOffset;
    if (caret <= window.NovalistEditorState.mentionState.atOffset) {
        closeMentionPicker(true);
        return;
    }
    const query = txt.slice(window.NovalistEditorState.mentionState.atOffset + 1, caret);
    // Abort if query contains a space (mention names are single-word for the picker).
    if (/\s/.test(query)) {
        closeMentionPicker(true);
        return;
    }
    window.NovalistEditorState.mentionState.query = query;
    renderMentionPicker();
}

function getMentionPositionRect() {
    // Position the picker right below the `@` character.
    if (!window.NovalistEditorState.mentionState) return null;
    try {
        const r = document.createRange();
        r.setStart(window.NovalistEditorState.mentionState.node, window.NovalistEditorState.mentionState.atOffset);
        r.setEnd(window.NovalistEditorState.mentionState.node, Math.min(window.NovalistEditorState.mentionState.atOffset + 1, (window.NovalistEditorState.mentionState.node.textContent || '').length));
        return r.getBoundingClientRect();
    } catch { return null; }
}

function scoreCandidate(cand, query) {
    if (!query) return 1;
    const q = query.toLowerCase();
    const name = (cand.matchedText || cand.primaryName || '').toLowerCase();
    if (name === q) return 1000;
    if (name.startsWith(q)) return 600 - (cand.isAlias ? 5 : 0);
    if (name.includes(q)) return 300 - (cand.isAlias ? 5 : 0);
    // Subsequence
    let i = 0;
    for (const ch of name) { if (ch === q[i]) i++; if (i === q.length) break; }
    if (i === q.length) return 100 - name.length - (cand.isAlias ? 5 : 0);
    return -1;
}

function renderMentionPicker() {
    if (!window.NovalistEditorState.mentionState) return;

    window.NovalistEditorState.mentionFiltered = window.NovalistEditorState.mentionCandidates
        .map(c => ({ c: c, score: scoreCandidate(c, window.NovalistEditorState.mentionState.query) }))
        .filter(p => p.score > 0)
        .sort((a, b) => b.score - a.score)
        .slice(0, 20)
        .map(p => p.c);

    window.NovalistEditorState.mentionSelectedIndex = 0;
    window.NovalistEditorState.mentionPicker.innerHTML = '';

    // A non-empty query can always be turned into a brand-new entity, so the
    // picker never dead-ends on an unknown name.
    window.NovalistEditorState.mentionCreateVisible = window.NovalistEditorState.mentionState.query.trim().length > 0;

    if (window.NovalistEditorState.mentionFiltered.length === 0 && !window.NovalistEditorState.mentionCreateVisible) {
        const empty = document.createElement('div');
        empty.className = 'nv-mp-empty';
        empty.textContent = window.NovalistEditorState.mentionLabels.noMatches || 'No matches';
        window.NovalistEditorState.mentionPicker.appendChild(empty);
    } else {
        window.NovalistEditorState.mentionFiltered.forEach((c, idx) => {
            const row = document.createElement('div');
            row.className = 'nv-mp-row' + (idx === 0 ? ' selected' : '');
            const name = document.createElement('div');
            name.className = 'nv-mp-name';
            name.textContent = c.primaryName;
            if (c.isAlias) {
                const hint = document.createElement('span');
                hint.className = 'nv-mp-alias-hint';
                hint.textContent = '(' + c.matchedText + ')';
                name.appendChild(hint);
            }
            row.appendChild(name);
            if (c.subtitle) {
                const sub = document.createElement('div');
                sub.className = 'nv-mp-subtitle';
                sub.textContent = c.subtitle;
                row.appendChild(sub);
            }
            row.addEventListener('mousedown', (ev) => {
                ev.preventDefault();
                confirmMentionAt(idx);
            });
            window.NovalistEditorState.mentionPicker.appendChild(row);
        });
    }

    if (window.NovalistEditorState.mentionCreateVisible) {
        const createRow = document.createElement('div');
        createRow.className = 'nv-mp-create' + (window.NovalistEditorState.mentionFiltered.length === 0 ? ' selected' : '');
        const template = window.NovalistEditorState.mentionLabels.create || 'Create "{name}"';
        createRow.textContent = template.replace('{name}', window.NovalistEditorState.mentionState.query.trim());
        createRow.addEventListener('mousedown', (ev) => {
            ev.preventDefault();
            confirmMentionAt(window.NovalistEditorState.mentionFiltered.length);
        });
        window.NovalistEditorState.mentionPicker.appendChild(createRow);
        if (window.NovalistEditorState.mentionFiltered.length === 0) window.NovalistEditorState.mentionSelectedIndex = 0;
    }

    // Position picker right below the `@` char.
    const rect = getMentionPositionRect();
    if (rect) {
        window.NovalistEditorState.mentionPicker.style.left = (rect.left + window.scrollX) + 'px';
        window.NovalistEditorState.mentionPicker.style.top = (rect.bottom + window.scrollY + 4) + 'px';
        window.NovalistEditorState.mentionPicker.classList.add('visible');
    }
}

function moveMentionSelection(delta) {
    const total = window.NovalistEditorState.mentionFiltered.length + (window.NovalistEditorState.mentionCreateVisible ? 1 : 0);
    if (total === 0) return;
    window.NovalistEditorState.mentionSelectedIndex = (window.NovalistEditorState.mentionSelectedIndex + delta + total) % total;
    Array.from(window.NovalistEditorState.mentionPicker.children).forEach((row, idx) => {
        row.classList.toggle('selected', idx === window.NovalistEditorState.mentionSelectedIndex);
    });
    // Scroll into view
    const sel = window.NovalistEditorState.mentionPicker.children[window.NovalistEditorState.mentionSelectedIndex];
    if (sel && sel.scrollIntoView) sel.scrollIntoView({ block: 'nearest' });
}

function confirmMentionAt(idx) {
    if (!window.NovalistEditorState.mentionState) return;
    // The row past the last match is "Create <name>".
    if (window.NovalistEditorState.mentionCreateVisible && idx === window.NovalistEditorState.mentionFiltered.length) { requestMentionCreate(); return; }
    if (idx < 0 || idx >= window.NovalistEditorState.mentionFiltered.length) return;
    const cand = window.NovalistEditorState.mentionFiltered[idx];
    const displayText = cand.matchedText || cand.primaryName;

    const html = '<span class="nv-entity-mention"'
        + ' data-entity-id="' + escapeAttr(cand.entityId) + '"'
        + ' data-entity-type="' + escapeAttr(cand.entityType) + '"'
        + ' data-mention-source="' + (cand.isAlias ? 'alias' : 'name') + '">'
        + window.escapeHtml(displayText)
        + '</span>&nbsp;';

    if (!replaceMentionTokenWith(html)) { closeMentionPicker(true); return; }

    closeMentionPicker(false);
    window.queueContentChangedSoon();
}

/**
 * "Create <name>" was chosen. The typed `@name` token is swapped for a pending
 * placeholder right away (so the writer keeps typing without waiting), and the
 * host is asked to create the entity. It answers with resolvePendingMention().
 */
function requestMentionCreate() {
    if (!window.NovalistEditorState.mentionState) return;
    const name = window.NovalistEditorState.mentionState.query.trim();
    if (name.length === 0) { closeMentionPicker(true); return; }

    const pendingId = 'pm-' + (++window.NovalistEditorState.mentionPendingSeq) + '-' + Date.now();
    const html = '<span class="nv-mention-pending" data-pending-id="' + escapeAttr(pendingId) + '">'
        + window.escapeHtml(name)
        + '</span>';
    if (!replaceMentionTokenWith(html)) { closeMentionPicker(true); return; }

    closeMentionPicker(false);
    window.queueContentChangedSoon();
    window.sendMessage({ type: 'mentionCreateRequested', name: name, pendingId: pendingId });
}

/**
 * Host answer to a create request. With an entityId the placeholder becomes a
 * real mention span; without one (the writer cancelled, or creation failed) it
 * collapses back to the plain typed text.
 */
function resolvePendingMention(pendingId, entityId, entityType, displayText) {
    const span = document.querySelector('.nv-mention-pending[data-pending-id="' + cssEscape(pendingId) + '"]');
    if (!span) return;
    const text = displayText || span.textContent || '';

    if (!entityId) {
        span.replaceWith(document.createTextNode(text));
    } else {
        const mention = document.createElement('span');
        mention.className = 'nv-entity-mention';
        mention.setAttribute('data-entity-id', entityId);
        mention.setAttribute('data-entity-type', entityType || '');
        mention.setAttribute('data-mention-source', 'name');
        mention.textContent = text;
        span.replaceWith(mention);
    }
    window.queueContentChangedSoon();
}

/** Replaces the active `@query` token with html. Returns false when the caret
 *  selection could not be resolved. */
function replaceMentionTokenWith(html) {
    const sel = window.getSelection();
    if (!sel || sel.rangeCount === 0) return false;
    const range = document.createRange();
    range.setStart(window.NovalistEditorState.mentionState.node, window.NovalistEditorState.mentionState.atOffset);
    const endRange = sel.getRangeAt(0);
    range.setEnd(endRange.endContainer, endRange.endOffset);
    sel.removeAllRanges();
    sel.addRange(range);

    let inserted = false;
    try { inserted = document.execCommand('insertHTML', false, html); } catch (error) { console.warn("editor: replaceMentionTokenWith failed", error instanceof Error ? error.name : typeof error); }
    if (!inserted) {
        range.deleteContents();
        const tmp = document.createElement('div');
        tmp.replaceChildren(window.sanitizeSceneFragment(html));
        const frag = document.createDocumentFragment();
        while (tmp.firstChild) frag.appendChild(tmp.firstChild);
        range.insertNode(frag);
    }
    return true;
}

/** Minimal CSS attribute-value escape for the ids we generate. */
function cssEscape(value) {
    return String(value).replace(/["\\]/g, '\\$&');
}

function escapeAttr(s) {
    return String(s).replace(/[&<>"']/g, c =>
        ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' })[c]);
}

Object.assign(window, {
    setMentionCandidates,
    isWordChar,
    detectMentionAtCaret,
    openMentionPicker,
    closeMentionPicker,
    updateMentionQuery,
    getMentionPositionRect,
    scoreCandidate,
    renderMentionPicker,
    moveMentionSelection,
    confirmMentionAt,
    requestMentionCreate,
    resolvePendingMention,
    replaceMentionTokenWith,
    cssEscape,
    escapeAttr
});
