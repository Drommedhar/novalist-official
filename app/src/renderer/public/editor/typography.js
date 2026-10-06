'use strict';

// ── Theme & Font ────────────────────────────────────────────────

function setTheme(...colors) {
    window.applyFrameTheme(colors, [
        '--bg', '--fg', '--caret', '--selection-bg', '--page-bg', '--page-fg',
        '--scrollbar-thumb', '--scrollbar-thumb-hover', '--scrollbar-thumb-active'
    ]);
    document.documentElement.style.setProperty('--caret', colors[2] || colors[1]);
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
    // Force-override via a dynamic <style> to beat any inline styles
    let s = document.getElementById('novalist-font-override');
    if (!s) {
        s = document.createElement('style');
        s.id = 'novalist-font-override';
        document.head.appendChild(s);
    }
    const f = family ? "'" + family + "', sans-serif" : 'var(--font-family)';
    const sz = size ? size + 'px' : 'var(--font-size)';
    s.textContent = '#editor, #editor * { font-family: ' + f + ' !important; font-size: ' + sz + ' !important; }';
}

function setBookWidth(enabled, widthPx) {
    const root = document.documentElement;
    root.style.setProperty('--book-width', enabled ? widthPx + 'px' : 'none');
}

function setPadding(horizontal, vertical) {
    const root = document.documentElement;
    if (horizontal != null) root.style.setProperty('--padding-h', horizontal + 'px');
    if (vertical != null) root.style.setProperty('--padding-v', vertical + 'px');
}

/**
 * Reading comfort. Leading, letter spacing, the gap between paragraphs and
 * first-line indent, none of which a colour-only theme can reach.
 */
function setReadingComfort(lineHeight, letterSpacing, paragraphSpacing, firstLineIndent) {
    const root = document.documentElement;
    root.style.setProperty('--line-height', lineHeight > 0 ? String(lineHeight) : '1.7');
    root.style.setProperty(
        '--letter-spacing', letterSpacing ? letterSpacing + 'px' : 'normal');
    root.style.setProperty(
        '--paragraph-spacing', (paragraphSpacing >= 0 ? paragraphSpacing : 0.75) + 'em');
    root.style.setProperty(
        '--first-line-indent', (firstLineIndent >= 0 ? firstLineIndent : 0) + 'em');
}

function setBookParagraphSpacing(enabled) {
    const editor = document.getElementById('editor');
    if (enabled) editor.classList.add('book-spacing');
    else editor.classList.remove('book-spacing');
}

function setLanguage(lang) {
    document.documentElement.lang = lang || 'en';
    // The prose surface carries its own lang so the platform spell checker picks
    // the right dictionary for the writing language rather than the UI language.
    window.NovalistEditorState.editor.lang = lang || 'en';
}

// Red underlines from the platform's own checker. Off by default in the markup
// so a writer who disabled it never sees a flash of underlines while the setting
// is still loading.
function setSpellCheck(enabled) {
    window.NovalistEditorState.editor.spellcheck = !!enabled;
    // Chromium only re-runs the check when the attribute changes on a focused,
    // live element; toggling contenteditable forces it to re-scan what is
    // already on screen instead of waiting for the next keystroke.
    // An inactive iframe remembers its last active element. Refreshing its
    // spelling must not take focus back from another pane or a settings field.
    if (document.hasFocus() && document.activeElement === window.NovalistEditorState.editor) {
        window.NovalistEditorState.editor.blur();
        window.NovalistEditorState.editor.focus();
    }
}

Object.assign(window, {
    setTheme,
    setFont,
    setBookWidth,
    setPadding,
    setReadingComfort,
    setBookParagraphSpacing,
    setLanguage,
    setSpellCheck
});
