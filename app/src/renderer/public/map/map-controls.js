import { mapState } from './map-state.js';
import { walkNodes } from './map-layers.js';

export function bbButton(label, fn, opts) {
    opts = opts || {};
    const b = document.createElement('button');
    b.textContent = label;
    if (opts.primary) b.classList.add('primary');
    if (opts.danger)  b.classList.add('danger');
    if (opts.active)  b.classList.add('active');
    if (opts.disabled) b.disabled = true;
    if (fn) b.addEventListener('click', fn);
    return b;
}

export function bbHint(text) {
    const s = document.createElement('span');
    s.className = 'bb-hint';
    s.textContent = text || '';
    return s;
}

export function bbKbd(text) {
    const s = document.createElement('span');
    s.className = 'bb-kbd';
    s.textContent = text;
    return s;
}

export function bbField(label, child) {
    const wrap = document.createElement('span');
    wrap.className = 'bb-field';
    if (label) { const l = document.createElement('span'); l.className = 'bb-flabel'; l.textContent = label; wrap.appendChild(l); }
    wrap.appendChild(child);
    return wrap;
}

export function bbColor(hex, onChange, label) {
    const i = document.createElement('input');
    i.type = 'color'; i.value = hex || '#000000';
    i.addEventListener('input', () => onChange(i.value));
    return bbField(label, i);
}

export function bbNumber(value, min, max, step, onChange, label) {
    const i = document.createElement('input');
    i.type = 'number'; i.min = min; i.max = max; i.step = step; i.value = value;
    i.addEventListener('change', () => { const v = parseFloat(i.value); if (!isNaN(v)) onChange(v); });
    return bbField(label, i);
}

export function bbSlider(value, min, max, step, onChange, label) {
    const wrap = document.createElement('span');
    wrap.className = 'bb-field';
    if (label) { const l = document.createElement('span'); l.className = 'bb-flabel'; l.textContent = label; wrap.appendChild(l); }
    const i = document.createElement('input');
    i.type = 'range'; i.min = min; i.max = max; i.step = step; i.value = value;
    const out = document.createElement('span');
    out.className = 'bb-flabel'; out.textContent = (+value).toFixed(step < 1 ? 2 : 0);
    i.addEventListener('input', () => {
        const v = parseFloat(i.value);
        out.textContent = v.toFixed(step < 1 ? 2 : 0);
        onChange(v);
    });
    wrap.appendChild(i); wrap.appendChild(out);
    return wrap;
}

export function bbCheckbox(label, checked, onChange) {
    const wrap = document.createElement('label');
    wrap.className = 'bb-field';
    wrap.style.cursor = 'pointer';
    const i = document.createElement('input');
    i.type = 'checkbox'; i.checked = !!checked;
    i.addEventListener('change', () => onChange(i.checked));
    const txt = document.createElement('span');
    txt.className = 'bb-flabel'; txt.textContent = label || '';
    wrap.appendChild(i); wrap.appendChild(txt);
    return wrap;
}

export function bbSelect(value, options, onChange, label) {
    // options: array of [value, label] OR plain strings
    const sel = document.createElement('select');
    for (const opt of (options || [])) {
        const o = document.createElement('option');
        if (Array.isArray(opt)) { o.value = opt[0]; o.textContent = opt[1]; }
        else                    { o.value = opt;    o.textContent = opt; }
        if (o.value === (value || '')) o.selected = true;
        sel.appendChild(o);
    }
    sel.addEventListener('change', () => onChange(sel.value));
    return bbField(label, sel);
}

export function bbToggleGroup(value, options, onChange) {
    const wrap = document.createElement('span');
    wrap.className = 'bb-toggle';
    for (const [v, lbl] of options) {
        const b = document.createElement('button');
        b.textContent = lbl;
        if (v === value) b.classList.add('active');
        b.addEventListener('click', () => onChange(v));
        wrap.appendChild(b);
    }
    return wrap;
}

export function bbText(value, onChange, label, placeholder) {
    const i = document.createElement('input');
    i.type = 'text'; i.value = value || ''; if (placeholder) i.placeholder = placeholder;
    let timer = null;
    i.addEventListener('input', () => {
        clearTimeout(timer);
        timer = setTimeout(() => onChange(i.value), 400);
    });
    i.addEventListener('blur', () => { clearTimeout(timer); onChange(i.value); });
    return bbField(label, i);
}

export function bbPopover(label, populate) {
    const btn = document.createElement('button');
    btn.textContent = label;
    btn.addEventListener('click', e => {
        e.stopPropagation();
        const existing = document.querySelector('.bb-popover');
        if (existing) { existing.remove(); return; }
        const pop = document.createElement('div');
        pop.className = 'bb-popover';
        populate(pop);
        document.body.appendChild(pop);
        const r = btn.getBoundingClientRect();
        const pr = pop.getBoundingClientRect();
        pop.style.left = Math.max(8, Math.min(window.innerWidth - pr.width - 8, r.left)) + 'px';
        pop.style.top = (r.top - pr.height - 6) + 'px';
        const close = ev => {
            if (pop.contains(ev.target) || btn.contains(ev.target)) return;
            pop.remove();
            window.removeEventListener('mousedown', close, true);
            window.removeEventListener('wheel', closeAny);
            window.removeEventListener('blur', closeAny);
        };
        const closeAny = () => {
            pop.remove();
            window.removeEventListener('mousedown', close, true);
            window.removeEventListener('wheel', closeAny);
            window.removeEventListener('blur', closeAny);
        };
        setTimeout(() => {
            window.addEventListener('mousedown', close, true);
            window.addEventListener('wheel', closeAny, { passive: true });
            window.addEventListener('blur', closeAny);
        }, 0);
    });
    return btn;
}

export function initializeMapControls() {
    mapState.bb = {
        el:     document.getElementById('nv-bottom-bar'),
        left:   document.querySelector('#nv-bottom-bar .bb-left'),
        center: document.querySelector('#nv-bottom-bar .bb-center'),
        right:  document.querySelector('#nv-bottom-bar .bb-right'),
    };
    mapState.MODE_ACCENT = {
        spline:    '#6fd0ff',
        terrain:   '#7bd88f',
        border:    '#d8a24a',
        clip:      '#ff9c42',
        floorplan: '#d8a24a',
        building:  '#ffae42',
        pin:       'var(--pin)',
        label:     '#cdd6f4',
        image:     '#cdd6f4',
        idle:      '#3a3a4a',
    };
}

// Layer options derived live from mapData on every render. No host push needed.
export function layerSelectOptions() {
    const out = [];
    walkNodes(mapState.mapData.layers, n => { out.push([n.id, n.name || n.id]); });
    return out;
}

export function bbLayerSelect(kind, elementId, currentLayerId) {
    const opts = layerSelectOptions();
    if (opts.length < 2) return null; // hide if only one (or zero) layers
    return bbSelect(currentLayerId || '', opts,
                    v => window.moveMapElementToLayer(kind, elementId, v),
                    mapState.mapStrings.bbLayer || 'Layer');
}

export function publishLayerCount() {
    Object.defineProperty(window, '__layerCount', {
        get() { return layerSelectOptions().length; }
    });
}
