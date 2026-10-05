'use strict';

const wrapper = document.getElementById('manuscript-wrapper');
let isSettingContent = false;
let bookSpacingEnabled = false;
let contentChangeTimers = {};
let lastHtmlMap = {};

// ── C# ↔ JS Bridge ─────────────────────────────────────────────

function sendMessage(msg) {
    try {
        const json = JSON.stringify(msg);
        if (typeof invokeCSharpAction === 'function') {
            invokeCSharpAction(json);
        } else if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(json);
        } else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.webview) {
            window.webkit.messageHandlers.webview.postMessage(json);
        } else if (window.parent && window.parent !== window) {
            // Electron shell: same-origin iframe; parent bridges to the backend.
            window.parent.postMessage({ novalistManuscript: json }, '*');
        }
    } catch (_) {}
}

// ── Manuscript Loading ──────────────────────────────────────────

function setManuscript(sectionsJson) {
    isSettingContent = true;
    wrapper.innerHTML = '';
    lastHtmlMap = {};

    let sections;
    try { sections = JSON.parse(sectionsJson); } catch (_) { sections = []; }

    for (const section of sections) {
        const sectionEl = document.createElement('div');
        sectionEl.className = 'chapter-section';

        // Chapter header
        const header = document.createElement('div');
        header.className = 'chapter-header';

        if (section.act) {
            const actEl = document.createElement('div');
            actEl.className = 'chapter-act';
            actEl.textContent = section.act;
            header.appendChild(actEl);
        }

        const titleRow = document.createElement('div');
        titleRow.className = 'chapter-title-row';

        const titleEl = document.createElement('span');
        titleEl.className = 'chapter-title';
        titleEl.textContent = section.chapterTitle;
        titleRow.appendChild(titleEl);

        const statusEl = document.createElement('span');
        statusEl.className = 'chapter-status';
        statusEl.textContent = section.status;
        statusEl.dataset.chapterGuid = section.chapterGuid;
        statusEl.addEventListener('click', () => {
            sendMessage({ type: 'cycleStatus', chapterGuid: section.chapterGuid });
        });
        titleRow.appendChild(statusEl);

        header.appendChild(titleRow);
        sectionEl.appendChild(header);

        // Scenes
        for (const scene of section.scenes) {
            const block = document.createElement('div');
            block.className = 'scene-block';

            const sceneHeader = document.createElement('div');
            sceneHeader.className = 'scene-header';

            const sceneTitle = document.createElement('span');
            sceneTitle.className = 'scene-title';
            sceneTitle.textContent = scene.title;
            sceneTitle.addEventListener('click', () => {
                sendMessage({ type: 'openScene', chapterGuid: section.chapterGuid, sceneId: scene.sceneId });
            });
            sceneHeader.appendChild(sceneTitle);

            const wordCount = document.createElement('span');
            wordCount.className = 'scene-wordcount';
            wordCount.id = 'wc-' + scene.sceneId;
            wordCount.textContent = scene.wordCount + ' words';
            sceneHeader.appendChild(wordCount);

            block.appendChild(sceneHeader);

            const editor = document.createElement('div');
            editor.className = 'scene-editor' + (bookSpacingEnabled ? ' book-spacing' : '');
            editor.contentEditable = 'true';
            editor.spellcheck = false;
            editor.dataset.sceneId = scene.sceneId;
            editor.dataset.chapterGuid = section.chapterGuid;

            const html = sanitizeSceneHtml(scene.html) || '<p><br></p>';
            editor.innerHTML = html;
            refreshSceneBreakClasses(editor);
            lastHtmlMap[scene.sceneId] = editor.innerHTML;

            editor.addEventListener('input', () => onSceneInput(editor));
            editor.addEventListener('paste', onPaste);
            editor.addEventListener('keydown', onKeyDown);
            editor.addEventListener('focus', () => {
                sendMessage({ type: 'sceneFocused', sceneId: scene.sceneId, chapterGuid: section.chapterGuid });
            });

            block.appendChild(editor);
            sectionEl.appendChild(block);
        }

        wrapper.appendChild(sectionEl);
    }

    // Footer
    const footer = document.createElement('div');
    footer.className = 'manuscript-footer';
    footer.id = 'manuscript-footer';
    wrapper.appendChild(footer);

    isSettingContent = false;
    updateFooter();
}

// Fire ready only once on initial page load
sendMessage({ type: 'ready' });

// ── Scene Content Changes ───────────────────────────────────────

/** Marks ornament-only blocks so first-line indent is reserved for prose. */
function refreshSceneBreakClasses(root) {
    root.querySelectorAll('p, div').forEach(function (block) {
        if (block.querySelector('p, div, h1, h2, h3, h4, h5, h6, blockquote, li')) return;
        const text = block.textContent || '';
        block.classList.toggle(
            'nv-scene-break',
            /^[\s\u00a0]*[*\-#•_~](?:[\s\u00a0]*[*\-#•_~])*[\s\u00a0]*$/u.test(text));
    });
}

function captureSceneChange(editor) {
    const sceneId = editor.dataset.sceneId;
    const chapterGuid = editor.dataset.chapterGuid;
    refreshSceneBreakClasses(editor);
    const html = editor.innerHTML;
    if (html === lastHtmlMap[sceneId]) return null;
    lastHtmlMap[sceneId] = html;
    const plainText = editor.innerText || '';
    const wordCount = countWords(plainText);

    // Update inline word count
    const wcEl = document.getElementById('wc-' + sceneId);
    if (wcEl) wcEl.textContent = wordCount + ' words';
    updateFooter();
    return { sceneId, chapterGuid, html, plainText, wordCount };
}

function reportSceneChange(editor) {
    const change = captureSceneChange(editor);
    if (change) sendMessage(Object.assign({ type: 'sceneContentChanged' }, change));
}

function onSceneInput(editor) {
    if (isSettingContent) return;
    const sceneId = editor.dataset.sceneId;

    if (contentChangeTimers[sceneId]) clearTimeout(contentChangeTimers[sceneId]);
    contentChangeTimers[sceneId] = setTimeout(() => {
        delete contentChangeTimers[sceneId];
        reportSceneChange(editor);
    }, 50);
}

/** Flushes every scene's short iframe debounce before the host saves. */
function flushPendingChanges() {
    if (isSettingContent) return [];
    const changes = [];
    wrapper.querySelectorAll('.scene-editor').forEach(function (editor) {
        const sceneId = editor.dataset.sceneId;
        if (contentChangeTimers[sceneId]) clearTimeout(contentChangeTimers[sceneId]);
        delete contentChangeTimers[sceneId];
        const change = captureSceneChange(editor);
        if (change) changes.push(change);
    });
    return changes;
}

function countWords(text) {
    if (!text || !text.trim()) return 0;
    return text.trim().split(/\s+/).length;
}

function updateFooter() {
    const editors = wrapper.querySelectorAll('.scene-editor');
    let totalWords = 0;
    let totalScenes = editors.length;
    for (const ed of editors) {
        totalWords += countWords(ed.innerText || '');
    }
    const chapters = wrapper.querySelectorAll('.chapter-section').length;
    const footer = document.getElementById('manuscript-footer');
    if (footer) {
        const readingTime = Math.max(1, Math.round(totalWords / 250));
        footer.textContent = totalWords.toLocaleString() + ' words · ' + totalScenes + ' scenes · ~' + readingTime + ' min read';
    }
}

// ── Update single scene content from C# ─────────────────────────

function updateSceneContent(sceneId, html) {
    isSettingContent = true;
    const editor = wrapper.querySelector('.scene-editor[data-scene-id="' + sceneId + '"]');
    if (editor) {
        editor.innerHTML = sanitizeSceneHtml(html) || '<p><br></p>';
        refreshSceneBreakClasses(editor);
        lastHtmlMap[sceneId] = editor.innerHTML;
    }
    isSettingContent = false;
}

// ── Theme & Font ────────────────────────────────────────────────

function setTheme(bg, fg, caretColor, selectionBg, accent, subtle, divider, scrollbarThumb, scrollbarThumbHover, scrollbarThumbActive) {
    const root = document.documentElement;
    root.style.setProperty('--bg', bg);
    root.style.setProperty('--fg', fg);
    root.style.setProperty('--caret', caretColor || fg);
    if (selectionBg) root.style.setProperty('--selection-bg', selectionBg);
    if (accent) root.style.setProperty('--accent', accent);
    if (subtle) root.style.setProperty('--subtle', subtle);
    if (divider) root.style.setProperty('--divider', divider);
    // Scrollbars are browser-painted, so the host hands us its resolved tokens.
    if (scrollbarThumb) root.style.setProperty('--scrollbar-thumb', scrollbarThumb);
    if (scrollbarThumbHover) root.style.setProperty('--scrollbar-thumb-hover', scrollbarThumbHover);
    if (scrollbarThumbActive) root.style.setProperty('--scrollbar-thumb-active', scrollbarThumbActive);
}

/** Leading, letter spacing and first-line indent, same contract as the scene editor. */
function setReadingComfort(lineHeight, letterSpacing, firstLineIndent) {
    const root = document.documentElement;
    root.style.setProperty('--line-height', lineHeight > 0 ? String(lineHeight) : '1.6');
    root.style.setProperty(
        '--letter-spacing', letterSpacing ? letterSpacing + 'px' : 'normal');
    root.style.setProperty(
        '--first-line-indent', (firstLineIndent >= 0 ? firstLineIndent : 0) + 'em');
}

function setFont(family, size) {
    const root = document.documentElement;
    if (family) {
        const val = "'" + family + "', sans-serif";
        root.style.setProperty('--font-family', val);
    }
    if (size) {
        root.style.setProperty('--font-size', size + 'px');
    }
    let s = document.getElementById('novalist-font-override');
    if (!s) {
        s = document.createElement('style');
        s.id = 'novalist-font-override';
        document.head.appendChild(s);
    }
    const f = family ? "'" + family + "', sans-serif" : 'var(--font-family)';
    const sz = size ? size + 'px' : 'var(--font-size)';
    s.textContent = '.scene-editor, .scene-editor * { font-family: ' + f + ' !important; font-size: ' + sz + ' !important; }';
}

function setBookParagraphSpacing(enabled) {
    bookSpacingEnabled = enabled;
    const editors = wrapper.querySelectorAll('.scene-editor');
    for (const ed of editors) {
        if (enabled) ed.classList.add('book-spacing');
        else ed.classList.remove('book-spacing');
    }
}

function setLanguage(lang) {
    document.documentElement.lang = lang || 'en';
}

// ── Paste Sanitization ──────────────────────────────────────────

function onPaste(e) {
    const html = e.clipboardData?.getData('text/html');
    if (html) {
        e.preventDefault();
        document.execCommand('insertHTML', false, sanitizePastedHtml(html));
    }
}

function sanitizePastedHtml(html) {
    const tmp = document.createElement('div');
    tmp.innerHTML = sanitizeSceneHtml(html);
    const allElements = tmp.querySelectorAll('*');
    for (const el of allElements) {
        const tag = el.tagName.toLowerCase();
        const allowedTags = ['b', 'i', 'u', 'strong', 'em', 'p', 'br', 'div', 'span', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6'];
        if (!allowedTags.includes(tag)) {
            el.replaceWith(...el.childNodes);
            continue;
        }
        const style = el.getAttribute('style');
        while (el.attributes.length > 0) el.removeAttribute(el.attributes[0].name);
        if (style) {
            const allowed = {};
            if (/font-weight\s*:\s*bold/i.test(style)) allowed['font-weight'] = 'bold';
            if (/font-style\s*:\s*italic/i.test(style)) allowed['font-style'] = 'italic';
            if (/text-decoration\s*:\s*underline/i.test(style)) allowed['text-decoration'] = 'underline';
            const alignMatch = style.match(/text-align\s*:\s*(left|center|right|justify)/i);
            if (alignMatch) allowed['text-align'] = alignMatch[1];
            const parts = Object.entries(allowed).map(([k, v]) => k + ':' + v);
            if (parts.length > 0) el.setAttribute('style', parts.join(';'));
        }
    }
    return tmp.innerHTML;
}

// ── Keyboard ────────────────────────────────────────────────────

function onKeyDown(e) {
    // Forward function keys (F1-F12) and Esc unconditionally so host hotkeys
    // like F11 (focus mode) work while focus is in the editor.
    var isFunctionKey = /^F\d{1,2}$/.test(e.code);
    if (isFunctionKey || e.key === 'Escape') {
        e.preventDefault();
        sendMessage({
            type: 'hotkey',
            key: e.key,
            code: e.code,
            ctrlKey: e.ctrlKey,
            shiftKey: e.shiftKey,
            altKey: e.altKey
        });
        return;
    }

    // Forward modifier key combinations to the host app for hotkey dispatch.
    if ((e.ctrlKey || e.altKey) && !isTextEditingShortcut(e)) {
        e.preventDefault();
        sendMessage({
            type: 'hotkey',
            key: e.key,
            code: e.code,
            ctrlKey: e.ctrlKey,
            shiftKey: e.shiftKey,
            altKey: e.altKey
        });
    }
}

function isTextEditingShortcut(e) {
    if (!e.ctrlKey) return false;
    var k = e.key.toLowerCase();
    if (!e.shiftKey && !e.altKey) return 'acvxzy'.includes(k);
    if (e.shiftKey && !e.altKey && k === 'z') return true;
    return false;
}

// ── Ready Signal ────────────────────────────────────────────────
sendMessage({ type: 'ready' });
