const wrap = document.getElementById('wrap');
let speakingEls = [];
let selectedEls = [];
let progressEls = [];

function sendMessage(payload) {
    parent.postMessage({ novalistNarration: JSON.stringify(payload) }, '*');
}

/**
 * Draws the book.
 *
 * The scene HTML arrives already marked up by the host - a span round every
 * segment, keyed - so this page never has to decide where a line starts. It
 * paints, and it reports what was clicked.
 */
function setBook(json) {
    const book = JSON.parse(json);
    wrap.textContent = '';

    if (!book.chapters || book.chapters.length === 0) {
        const empty = document.createElement('div');
        empty.className = 'nl-empty';
        empty.textContent = book.emptyLabel || '';
        wrap.appendChild(empty);
        return;
    }

    for (const chapter of book.chapters) {
        const section = document.createElement('section');
        section.className = 'nl-chapter';
        section.dataset.chapterGuid = chapter.guid;

        const head = document.createElement('div');
        head.className = 'nl-chapter-head';
        if (chapter.act) {
            const act = document.createElement('div');
            act.className = 'nl-act';
            act.textContent = chapter.act;
            head.appendChild(act);
        }
        const title = document.createElement('div');
        title.className = 'nl-chapter-title';
        title.textContent = chapter.title;
        head.appendChild(title);
        section.appendChild(head);

        for (const scene of chapter.scenes) {
            const block = document.createElement('div');
            block.className = 'nl-scene';
            block.dataset.sceneId = scene.sceneId;
            block.dataset.chapterGuid = scene.chapterGuid;

            if (scene.sceneTitle) {
                const sceneTitle = document.createElement('div');
                sceneTitle.className = 'nl-scene-title';
                sceneTitle.textContent = scene.sceneTitle;
                block.appendChild(sceneTitle);
            }

            const prose = document.createElement('div');
            prose.className = 'nl-prose';
            // The host's own markup, and the writer's own words. Read-only:
            // this is where the book is listened to, not a second place to
            // edit it.
            prose.replaceChildren(window.sanitizeSceneFragment(scene.html));
            block.appendChild(prose);
            section.appendChild(block);

            paint(prose, scene, book.colours || {}, book.narratorColour);
        }

        wrap.appendChild(section);
    }
}

/** Gives each marker its speaker's colour and something to hover. */
function paint(prose, scene, colours, narratorColour) {
    const bySegment = {};
    for (const segment of scene.segments || []) bySegment[segment.key] = segment;

    const markers = prose.querySelectorAll('[data-nl-seg]');
    for (const el of markers) {
        const segment = bySegment[el.getAttribute('data-nl-seg')];
        if (!segment) continue;
        const colour = segment.speakerId ? colours[segment.speakerId] : narratorColour;
        if (colour) el.style.setProperty('--tint', colour);
        if (segment.kind === 'Dialogue' && !segment.speakerId) {
            el.setAttribute('data-nl-unknown', '1');
        }
        el.dataset.sceneId = scene.sceneId;
        el.dataset.chapterGuid = scene.chapterGuid;
        if (segment.label) el.title = segment.label;
    }
}

wrap.addEventListener('click', function (event) {
    const target = event.target;
    const marker = target && target.closest ? target.closest('[data-nl-seg]') : null;
    if (!marker) return;
    sendMessage({
        type: 'segmentClicked',
        key: marker.getAttribute('data-nl-seg'),
        sceneId: marker.dataset.sceneId,
        chapterGuid: marker.dataset.chapterGuid
    });
});

/** Marks the segment being read, and keeps it on screen. */
function setSpeaking(sceneId, key) {
    for (const el of speakingEls) el.classList.remove('nl-speaking');
    speakingEls = [];
    if (!key) return;
    speakingEls = Array.prototype.slice.call(markersFor(sceneId, key));
    for (const el of speakingEls) el.classList.add('nl-speaking');
    if (speakingEls.length > 0) keepOnScreen(speakingEls[0]);
}

/**
 * Marks what has been made and what is being made.
 *
 * Both arrive as key lists rather than as a diff, because the reading is
 * rebuilt from scratch whenever the book changes and a diff against a page that
 * has been redrawn is a diff against nothing.
 */
function setProgress(readyJson, renderingJson) {
    for (const el of progressEls) el.classList.remove('nl-ready', 'nl-rendering');
    progressEls = [];


    // Ready first, so a line that is both - made, and inside the window being
    // worked on - reads as the more urgent of the two.
    paintProgress(JSON.parse(readyJson || '[]'), 'nl-ready');
    paintProgress(JSON.parse(renderingJson || '[]'), 'nl-rendering');
}

function paintProgress(keys, className) {
    for (const key of keys) {
        const found = wrap.querySelectorAll('[data-nl-seg="' + escapeValue(key) + '"]');
        for (const marker of found) {
            marker.classList.add(className);
            progressEls.push(marker);
        }
    }
}

/** Marks the segment the writer is working on. */
function setSelected(sceneId, key, reveal) {
    for (const el of selectedEls) el.classList.remove('nl-selected');
    selectedEls = [];
    if (!key) return;
    selectedEls = Array.prototype.slice.call(markersFor(sceneId, key));
    for (const el of selectedEls) el.classList.add('nl-selected');
    if (reveal && selectedEls.length > 0) keepOnScreen(selectedEls[0]);
}

/** Brings a scene to the top of the frame - what "go to the one I am writing"
 *  means once the whole book is on a single strip. */
function revealScene(sceneId) {
    const block = sceneBlock(sceneId);
    if (block) block.scrollIntoView({ block: 'start', behavior: 'smooth' });
}

function sceneBlock(sceneId) {
    return wrap.querySelector('.nl-scene[data-scene-id="' + escapeValue(sceneId) + '"]');
}

function markersFor(sceneId, key) {
    const block = sceneBlock(sceneId);
    if (!block) return [];
    return block.querySelectorAll('[data-nl-seg="' + escapeValue(key) + '"]');
}

function keepOnScreen(el) {
    const box = el.getBoundingClientRect();
    const frame = wrap.getBoundingClientRect();
    if (box.top < frame.top + 60 || box.bottom > frame.bottom - 60) {
        el.scrollIntoView({ block: 'center', behavior: 'smooth' });
    }
}

function escapeValue(value) {
    return String(value == null ? '' : value).replace(/(["\\])/g, '\\$1');
}

// ── Theme, font, reading comfort: the manuscript frame's contract ──

function setTheme(...colors) {
    window.applyFrameTheme(colors, [
        '--bg', '--fg', '--accent', '--subtle', '--divider',
        '--scrollbar-thumb', '--scrollbar-thumb-hover', '--scrollbar-thumb-active'
    ]);
}

function setFont(family, size) {
    const root = document.documentElement;
    if (family) root.style.setProperty('--font-family', "'" + family + "', serif");
    if (size) root.style.setProperty('--font-size', size + 'px');
}

function setReadingComfort(lineHeight, letterSpacing) {
    const root = document.documentElement;
    root.style.setProperty('--line-height', lineHeight > 0 ? String(lineHeight) : '1.6');
    root.style.setProperty('--letter-spacing', letterSpacing ? letterSpacing + 'px' : 'normal');
}

function setLanguage(lang) {
    document.documentElement.lang = lang || 'en';
}

sendMessage({ type: 'ready' });

Object.assign(window, {
    setBook,
    setSpeaking,
    setProgress,
    setSelected,
    revealScene,
    setTheme,
    setFont,
    setReadingComfort,
    setLanguage
});
