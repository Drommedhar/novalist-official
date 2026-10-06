import { mapState } from './map-state.js';
import { bbField } from './map-controls.js';
import { removePopup, showPopup } from './popup-motion.js';

export function bbEntityPicker(id, type, onChange, label) {
    // Native <datalist> doesn't reliably render inside WebView2's chrome-less
    // surface (and would open *below* the input, getting clipped by the bottom
    // bar). Custom popover dropdown positioned ABOVE the input instead.
    const i = document.createElement('input');
    i.type = 'text';
    i.autocomplete = 'off';
    i.spellcheck = false;
    i.placeholder = mapState.mapStrings.pinEntityWatermark || '';
    const cur = (mapState.entityOptions || []).find(o => o.id === id);
    i.value = cur ? cur.name : (id || '');

    const resolveExact = (value) => {
        const opts = mapState.entityOptions || [];
        const v = (value || '').trim();
        if (!v) return null;
        return opts.find(o => o.name.toLowerCase() === v.toLowerCase()) || null;
    };

    // ── popover ──
    const picker = { pop: null, activeIndex: -1, filtered: [] };

    const closePop = () => {
        if (picker.pop) { removePopup(picker.pop); picker.pop = null; }
        picker.activeIndex = -1;
        window.removeEventListener('mousedown', onDocMouseDown, true);
        window.removeEventListener('wheel', closePop, true);
        window.removeEventListener('resize', closePop);
    };

    const onDocMouseDown = (e) => {
        if (picker.pop && (picker.pop.contains(e.target) || i.contains(e.target))) return;
        closePop();
    };

    const buildList = () => {
        const q = i.value.trim().toLowerCase();
        const opts = mapState.entityOptions || [];
        picker.filtered = q
            ? opts.filter(o => o.name.toLowerCase().includes(q))
            : opts.slice();
        picker.filtered.sort((a, b) => a.name.localeCompare(b.name));
        return picker.filtered.slice(0, 50);
    };

    const renderPop = () => {
        const items = buildList();
        const opening = !picker.pop;
        if (!picker.pop) {
            picker.pop = document.createElement('div');
            picker.pop.className = 'bb-entity-pop';
            document.body.appendChild(picker.pop);
        }
        picker.pop.replaceChildren();
        appendEntityPickerRows(picker.pop, items, picker.activeIndex, pick);
        // Position above the input.
        const r = i.getBoundingClientRect();
        picker.pop.style.left = r.left + 'px';
        picker.pop.style.minWidth = Math.max(r.width, 220) + 'px';
        // Render first to measure, then place.
        picker.pop.style.top = '0px';
        const ph = picker.pop.getBoundingClientRect().height;
        picker.pop.style.top = Math.max(8, r.top - ph - 4) + 'px';
        if (opening) showPopup(picker.pop);

        window.addEventListener('mousedown', onDocMouseDown, true);
        window.addEventListener('wheel', closePop, true);
        window.addEventListener('resize', closePop);
    };

    const pick = (o) => {
        i.value = o.name;
        i.classList.remove('bb-input-invalid');
        onChange(o.id, o.type);
        closePop();
    };

    wireEntityPicker(i, picker, { renderPop, resolveExact, pick, closePop }, onChange);
    return bbField(label, i);
}

export function appendEntityPickerRows(pop, items, activeIndex, pick) {
    if (items.length === 0) {
        const empty = document.createElement('div');
        empty.className = 'bb-entity-pop-empty';
        empty.textContent = mapState.mapStrings.pinEntityNoResults || 'No matches';
        pop.appendChild(empty);
    } else {
        items.forEach((o, idx) => {
            const row = document.createElement('div');
            row.className = 'bb-entity-pop-row';
            if (idx === activeIndex) row.classList.add('active');
            const name = document.createElement('span');
            name.className = 'bb-entity-pop-name';
            name.textContent = o.name;
            const kind = document.createElement('span');
            kind.className = 'bb-entity-pop-type';
            kind.textContent = o.type;
            row.appendChild(name);
            row.appendChild(kind);
            row.addEventListener('mousedown', (ev) => {
                ev.preventDefault();
                pick(o);
            });
            pop.appendChild(row);
        });
    }
}

export function wireEntityPicker(i, picker, actions, onChange) {
    const { renderPop, resolveExact, pick, closePop } = actions;
    i.addEventListener('focus', renderPop);
    i.addEventListener('input', () => {
        picker.activeIndex = -1;
        renderPop();
        if (!i.value.trim() || resolveExact(i.value))
            i.classList.remove('bb-input-invalid');
    });
    i.addEventListener('keydown', (e) => {
        if (!picker.pop) return;
        if (e.key === 'ArrowDown') {
            e.preventDefault();
            picker.activeIndex = Math.min((picker.filtered.length || 1) - 1, picker.activeIndex + 1);
            renderPop();
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            picker.activeIndex = Math.max(0, picker.activeIndex - 1);
            renderPop();
        } else if (e.key === 'Enter') {
            e.preventDefault();
            if (picker.activeIndex >= 0 && picker.filtered[picker.activeIndex])
                pick(picker.filtered[picker.activeIndex]);
            else {
                const exact = resolveExact(i.value);
                if (exact) pick(exact);
                else i.classList.add('bb-input-invalid');
            }
        } else if (e.key === 'Escape') {
            closePop();
        }
    });
    i.addEventListener('change', () => {
        // Blur commit — only succeed on exact match, otherwise flag invalid.
        const v = i.value.trim();
        if (!v) { onChange('', ''); i.classList.remove('bb-input-invalid'); return; }
        const exact = resolveExact(v);
        if (exact) { i.value = exact.name; onChange(exact.id, exact.type); i.classList.remove('bb-input-invalid'); }
        else { i.classList.add('bb-input-invalid'); }
    });
}

export function initializeEntityPicker() {
    mapState.entityOptions = [];
    window.setEntityOptions = (json) => {
        try { mapState.entityOptions = JSON.parse(json) || []; } catch { mapState.entityOptions = []; }
        const dl = document.getElementById('bb-entities');
        if (dl) {
            dl.replaceChildren();
            for (const o of mapState.entityOptions) {
                const opt = document.createElement('option');
                opt.value = o.name;
                dl.appendChild(opt);
            }
        }
        mapState.renderBottomBar();
    };
}
