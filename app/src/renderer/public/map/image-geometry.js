import { mapState } from './map-state.js';

export function applyImageTransform(el, img) {
    // Encode pan-less screen-space rect: image lives inside #world (translated by pan).
    // Per-image transform includes current zoom so changing zoom updates each image
    // independently, forcing the browser to repaint at the new effective resolution.
    const natW = el.naturalWidth || img.width || 1;
    const natH = el.naturalHeight || img.height || 1;
    el.style.width = natW + 'px';
    el.style.height = natH + 'px';
    const w = img.width || natW;
    const h = img.height || natH;
    const z = mapState.zoom;
    const sw = w * z;
    const sh = h * z;
    const cx = ((img.x || 0) + w / 2) * z;
    const cy = ((img.y || 0) + h / 2) * z;
    const rot = img.rotation || 0;
    el.style.transform =
        `translate(${cx}px, ${cy}px) rotate(${rot}deg) translate(${-sw / 2}px, ${-sh / 2}px) scale(${sw / natW}, ${sh / natH})`;
    applyClipPath(el, img, natW, natH);
}

export function applyClipPath(el, img, natW, natH) {
    if (!img.clipPolygon || img.clipPolygon.length < 3) {
        el.style.clipPath = '';
        return;
    }
    natW = natW || el.naturalWidth || 1;
    natH = natH || el.naturalHeight || 1;
    const pts = img.clipPolygon
        .map(p => `${(p.x / natW * 100).toFixed(3)}% ${(p.y / natH * 100).toFixed(3)}%`)
        .join(',');
    el.style.clipPath = `polygon(${pts})`;
}
