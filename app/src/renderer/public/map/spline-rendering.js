import { mapState } from './map-state.js';
import { walkNodes } from './map-layers.js';
import { splineProfile, sampleSpline, sampleNormals, bandPath, sampleVisual, segQuad, knotProfile, sampleBlend, lerpColor, segQuadBand, markingPath, knotBlendFactor, blendT, findSpline } from './spline-geometry.js';
import { sendMessage } from './map-messages.js';
import { emitSplineSelection } from './spline-draft.js';

export function renderSplines() {
    mapState.splineSvg.innerHTML = '';
    const casingG = document.createElementNS(mapState.SVG_NS, 'g');
    const fillG = document.createElementNS(mapState.SVG_NS, 'g');
    const markG = document.createElementNS(mapState.SVG_NS, 'g');
    const items = collectSplineRenderItems();
    renderSplineCasings(items, casingG);
    renderSplineFills(items, fillG);
    renderSplineMarkings(items, markG);
    mapState.splineSvg.appendChild(casingG);
    mapState.splineSvg.appendChild(fillG);
    mapState.splineSvg.appendChild(markG);
}

export function makeSplinePath(d, layerId, splineId) {
    const p = document.createElementNS(mapState.SVG_NS, 'path');
    p.setAttribute('d', d);
    p.classList.add('nv-spline-part');
    p.dataset.layerId = layerId;
    p.dataset.splineId = splineId;
    return p;
}

export function fillSplineSeam(p, color) {
    p.setAttribute('fill', color);
    p.setAttribute('stroke', color);
    p.setAttribute('stroke-width', '1');
    p.setAttribute('stroke-linejoin', 'round');
}

export function makeSplineCircle(cx, cy, r, fill, layerId, splineId) {
    const c = document.createElementNS(mapState.SVG_NS, 'circle');
    c.setAttribute('cx', cx.toFixed(1));
    c.setAttribute('cy', cy.toFixed(1));
    c.setAttribute('r', Math.max(0, r).toFixed(1));
    c.setAttribute('fill', fill);
    c.classList.add('nv-spline-part');
    c.dataset.layerId = layerId;
    c.dataset.splineId = splineId;
    return c;
}

export function interactiveSplinePart(p, splineId) {
    // Make a part clickable (delegated listeners on splineSvg do the rest).
    if (splineId !== '__draft__') { p.style.pointerEvents = 'auto'; p.style.cursor = 'pointer'; }
    return p;
}

export function hasUniformSplineProfile(sp) {
    return !(sp.points || []).some(p => p.typeOverride);
}

export function collectSplineRenderItems() {
    const items = [];
    walkNodes(mapState.mapData.layers, node => {
        for (const sp of node.splines || []) items.push({ node, sp });
    });
    if (mapState.splineDraft && mapState.splineDraft.points.length >= 2) {
        items.push({ node: { id: '__draft__' }, sp: {
            id: '__draft__', kind: mapState.splineDraft.kind, preset: mapState.splineDraft.preset,
            points: mapState.splineDraft.points, closed: false,
        } });
    }
    for (const it of items) {
        const baseProf = splineProfile(it.sp.kind, it.sp.preset);
        it.prof = baseProf;
        it.uniform = hasUniformSplineProfile(it.sp);
        it.samples = sampleSpline(it.sp.points, baseProf.straight, it.sp.closed);
        it.normals = it.samples.length >= 2 ? sampleNormals(it.samples, it.sp.closed) : null;
    }
    return items;
}

export function renderSplineCasings(items, casingG) {
    for (const { node, sp, prof, uniform, samples, normals } of items) {
        if (!normals) continue;
        if (uniform) {
            const p = makeSplinePath(bandPath(samples, normals, -1, 1, prof.casing.extra), node.id, sp.id);
            p.setAttribute('fill', sp.casingColor || prof.casing.color);
            casingG.appendChild(p);
        } else {
            for (let i = 0; i < samples.length - 1; i++) {
                const v = sampleVisual(sp, samples[i]);
                const p = makeSplinePath(segQuad(samples[i], normals[i], samples[i + 1], normals[i + 1],
                    1, v.casingExtra), node.id, sp.id);
                fillSplineSeam(p, sp.casingColor || v.casingColor);
                casingG.appendChild(p);
            }
        }
        // Junction caps: round the casing ends of an open spline so a road/river
        // stub doesn't terminate in a hard square edge.
        if (!sp.closed && samples.length >= 2) {
            for (const s of [samples[0], samples[samples.length - 1]]) {
                const v = sampleVisual(sp, s);
                const r = (s.w / 2 + v.casingExtra) * mapState.zoom;
                casingG.appendChild(makeSplineCircle(s.x * mapState.zoom, s.y * mapState.zoom, r,
                    sp.casingColor || v.casingColor, node.id, sp.id));
            }
        }
    }
}

export function renderSplineFills(items, fillG) {
    for (const { node, sp, prof, uniform, samples, normals } of items) {
        if (!normals) continue;
        // Closed rivers are bodies of water — fill the enclosed area, not just
        // the width-band ring. (Closed roads stay rings: roundabouts.)
        if (sp.closed && sp.kind === 'river' && samples.length >= 3) {
            let d = 'M';
            for (let i = 0; i < samples.length; i++)
                d += (i ? ' L' : '') + (samples[i].x * mapState.zoom).toFixed(1) + ',' + (samples[i].y * mapState.zoom).toFixed(1);
            d += ' Z';
            const prof0 = knotProfile(sp, 0);
            const surf = (prof0.bands || []).find(b => b.from <= 0 && b.to >= 0) || (prof0.bands || [])[0];
            const interior = makeSplinePath(d, node.id, sp.id);
            interior.setAttribute('fill', sp.fillColor || (surf ? surf.color : '#3b6ea5'));
            fillG.appendChild(interactiveSplinePart(interior, sp.id));
        }
        if (uniform) {
            for (const ba of prof.bands) {
                const p = makeSplinePath(bandPath(samples, normals, ba.from, ba.to, 0), node.id, sp.id);
                p.setAttribute('fill', sp.fillColor || ba.color);
                fillG.appendChild(interactiveSplinePart(p, sp.id));
            }
        } else {
            for (let i = 0; i < samples.length - 1; i++) {
                const { pA, pB, t } = sampleBlend(sp, samples[i]);
                const bands = pA.bands;
                for (let bi = 0; bi < bands.length; bi++) {
                    const ba = bands[bi];
                    const bb = pB.bands[Math.min(bi, pB.bands.length - 1)] || ba;
                    const color = sp.fillColor || lerpColor(ba.color, bb.color, t);
                    const p = makeSplinePath(segQuadBand(samples[i], normals[i],
                        samples[i + 1], normals[i + 1], ba.from, ba.to), node.id, sp.id);
                    fillSplineSeam(p, color);
                    fillG.appendChild(interactiveSplinePart(p, sp.id));
                }
            }
        }
        // Round the fill surface at open-spline ends to match the casing cap.
        if (!sp.closed && samples.length >= 2) {
            const ends = [
                { s: samples[0], prof: knotProfile(sp, 0) },
                { s: samples[samples.length - 1], prof: knotProfile(sp, sp.points.length - 1) },
            ];
            for (const { s, prof } of ends) {
                const bands = prof.bands || [];
                const surface = bands.find(b => b.from <= 0 && b.to >= 0) || bands[0];
                if (!surface) continue;
                const ext = Math.max(Math.abs(surface.from), Math.abs(surface.to));
                const r = (s.w / 2) * ext * mapState.zoom;
                fillG.appendChild(makeSplineCircle(s.x * mapState.zoom, s.y * mapState.zoom, r,
                    sp.fillColor || surface.color, node.id, sp.id));
            }
        }
    }
}

export function renderSplineMarkings(items, markG) {
    function emitMarking(mk, ss, sn, layerId, splineId, { opacity, colorOverride }) {
        const p = makeSplinePath(markingPath(ss, sn, mk.offset), layerId, splineId);
        p.setAttribute('fill', 'none');
        p.setAttribute('stroke', colorOverride || mk.color);
        p.setAttribute('stroke-width', mk.width);
        if (mk.dash) p.setAttribute('stroke-dasharray', mk.dash.join(' '));
        if (opacity != null && opacity < 1) p.setAttribute('opacity', opacity.toFixed(3));
        markG.appendChild(p);
    }
    for (const { node, sp, prof, samples, normals } of items) {
        if (!normals) continue;
        const mColor = sp.markingColor || null;
        for (const mk of presetEdgeMarkings(prof)) {
            emitMarking(mk, samples, normals, node.id, sp.id, { opacity: 1, colorOverride: mColor });
        }
        // Fast path: no per-knot centerline override → one centre marking over
        // the whole spline instead of one per knot-segment.
        if (!sp.points.some(p => p.markingStyle)) {
            for (const mk of centerMarkingsForStyle(prof, sp.markingStyle || ''))
                emitMarking(mk, samples, normals, node.id, sp.id, { opacity: 1, colorOverride: mColor });
            continue;
        }
        const N = sp.points.length;
        const segCount = sp.closed ? N : N - 1;
        for (let seg = 0; seg < segCount; seg++) {
            const nextIdx = (seg + 1) % N;
            const styleA = sp.points[seg].markingStyle || sp.markingStyle || '';
            const styleB = sp.points[nextIdx].markingStyle || sp.markingStyle || '';
            const marksA = centerMarkingsForStyle(prof, styleA);
            const marksB = centerMarkingsForStyle(prof, styleB);
            // Samples in this knot-segment, plus the first sample of the next so
            // the line is continuous across the knot.
            const segSamples = [], segNormals = [];
            for (let i = 0; i < samples.length; i++) {
                if (samples[i].seg === seg) { segSamples.push(samples[i]); segNormals.push(normals[i]); }
                else if (samples[i].seg === seg + 1 && segSamples.length) {
                    segSamples.push(samples[i]); segNormals.push(normals[i]); break;
                }
            }
            if (segSamples.length < 2) continue;
            if (styleA === styleB) {
                for (const mk of marksA) emitMarking(mk, segSamples, segNormals, node.id, sp.id, { opacity: 1, colorOverride: mColor });
                continue;
            }
            // Styles differ → cross-fade. Split the segment into sub-spans and
            // step the opacity between the two marking sets per blendT().
            const bf = Math.min(knotBlendFactor(sp.points[seg]), knotBlendFactor(sp.points[nextIdx]));
            const SUB = 8;
            const n = segSamples.length;
            for (let k = 0; k < SUB; k++) {
                const i0 = Math.floor(k / SUB * (n - 1));
                const i1 = Math.floor((k + 1) / SUB * (n - 1)) + 1;
                const ss = segSamples.slice(i0, i1);
                const sn = segNormals.slice(i0, i1);
                if (ss.length < 2) continue;
                const bt = blendT((k + 0.5) / SUB, bf);
                for (const mk of marksA) emitMarking(mk, ss, sn, node.id, sp.id, { opacity: 1 - bt, colorOverride: mColor });
                for (const mk of marksB) emitMarking(mk, ss, sn, node.id, sp.id, { opacity: bt, colorOverride: mColor });
            }
        }
    }
}

// Edge (offset != 0) markings from the preset — never restyled.
export function presetEdgeMarkings(prof) {
    return (prof.markings || []).filter(m => m.offset !== 0);
}

// Centerline marking set for a style: "" = the preset's own offset-0 markings,
// else a generated style. Used per knot-segment.
export function centerMarkingsForStyle(prof, style) {
    const base = prof.markings || [];
    const centerSrc = base.find(m => m.offset === 0);
    if (!style) return base.filter(m => m.offset === 0);
    const col = centerSrc ? centerSrc.color : '#ffffff';
    const gap = 0.045; // half-width fraction between paired lines
    if (style === 'none') return [];
    if (style === 'single') return [{ offset: 0, color: col, width: 1.8 }];
    if (style === 'dashed') return [{ offset: 0, color: col, width: 1.6, dash: [12, 10] }];
    if (style === 'double') return [
        { offset: -gap, color: col, width: 1.5 },
        { offset: gap, color: col, width: 1.5 }];
    if (style === 'solid-dashed') return [
        { offset: -gap, color: col, width: 1.5 },
        { offset: gap, color: col, width: 1.5, dash: [12, 10] }];
    return base.filter(m => m.offset === 0);
}

export function initializeSplineRendering() {
    window.setSplineMarkingStyle = (splineId, style) => {
        const sp = findSpline(splineId);
        if (!sp) return;
        if (style) sp.markingStyle = style;
        else delete sp.markingStyle;
        renderSplines();
        sendMessage({ type: 'mapChanged' });
    };
    window.setSplineColors = (splineId, casing, fill, marking) => {
        const sp = findSpline(splineId);
        if (!sp) return;
        const cleared = !casing && !fill && !marking;
        if (casing) sp.casingColor = casing; else delete sp.casingColor;
        if (fill) sp.fillColor = fill; else delete sp.fillColor;
        if (marking) sp.markingColor = marking; else delete sp.markingColor;
        renderSplines();
        // Reset (all empty) → re-emit so the host's colour pickers snap to the preset.
        if (cleared && mapState.splineEdit && mapState.splineEdit.splineId === splineId) emitSplineSelection();
        sendMessage({ type: 'mapChanged' });
    };
    window.setKnotMarkingStyle = (splineId, knotIdx, style) => {
        const sp = findSpline(splineId);
        if (!sp || !sp.points[knotIdx]) return;
        if (style) sp.points[knotIdx].markingStyle = style;
        else delete sp.points[knotIdx].markingStyle;
        renderSplines();
        if (mapState.splineEdit && mapState.splineEdit.splineId === splineId) emitSplineSelection();
        sendMessage({ type: 'mapChanged' });
    };
    window.setSplineCasingColor  = (id, hex) => { const sp = findSpline(id); if (sp) window.setSplineColors(id, hex || '', sp.fillColor || '', sp.markingColor || ''); };
    window.setSplineFillColor    = (id, hex) => { const sp = findSpline(id); if (sp) window.setSplineColors(id, sp.casingColor || '', hex || '', sp.markingColor || ''); };
    window.setSplineMarkingColor = (id, hex) => { const sp = findSpline(id); if (sp) window.setSplineColors(id, sp.casingColor || '', sp.fillColor || '', hex || ''); };
    window.resetSplineColors     = (id) => window.setSplineColors(id, '', '', '');
}
