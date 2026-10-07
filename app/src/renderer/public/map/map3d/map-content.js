import { projectImageUrl } from '../project-assets.js';
import * as THREE from "../three.webgpu.min.js";
import { mapHost } from './host.js';
import { DEG, RO_IMAGE, RO_TERRAIN } from './scene-settings.js';
import { makeLabelSprite, makePinMarker } from './building-meshes.js';

// Bounds over all current map content. Used to size the ground plane and to
// frame the camera on enter.
export function mapBounds() {
    let minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
    function acc(x, y) {
        if (x < minX) minX = x; if (y < minY) minY = y;
        if (x > maxX) maxX = x; if (y > maxY) maxY = y;
    }
    try {
        (function walk(nodes) {
            for (const n of nodes || []) {
                for (const im of n.images || []) {
                    acc(im.x, im.y);
                    acc(im.x + (im.width || 0), im.y + (im.height || 0));
                }
                for (const sp of n.splines || [])
                    for (const p of sp.points || []) acc(p.x, p.y);
                for (const sh of n.shapes || [])
                    for (const p of sh.points || []) acc(p.x, p.y);
                for (const bl of n.buildings || [])
                    for (const p of bl.footprint || []) acc(p.x, p.y);
                for (const pn of n.pins || []) acc(pn.x, pn.y);
                walk(n.children);
            }
        })((mapHost.mapData && (mapHost.mapData.layers || mapHost.mapData.groups)) || []);
    } catch (error) { console.warn("map3d: mapBounds failed", error instanceof Error ? error.name : typeof error); }
    if (minX > maxX) { minX = 0; minY = 0; maxX = 600; maxY = 600; }
    return { minX: minX, minY: minY, maxX: maxX, maxY: maxY };
}

// label sprites / pins — drawn over everything

export function mapLayers() {
    return (mapHost.mapData && (mapHost.mapData.layers || mapHost.mapData.groups)) || [];
}

// Hex (#rrggbb or number) → THREE.Color, with a fallback.
export function toColor(hex, fallback) {
    try {
        if (hex == null || hex === '') return new THREE.Color(fallback);
        return new THREE.Color(hex);
    } catch { return new THREE.Color(fallback); }
}

// Set of layer-node ids that are currently visible: parent `hidden` cascades,
// and a connected-set node shows only its active child. Per the locked
// decision, opacity and per-element / per-layer zoom ranges are ignored.
export function visibleLayerIds() {
    const ids = new Set();
    (function recurse(nodes, parentVisible) {
        for (const n of nodes || []) {
            const visible = parentVisible && !n.hidden;
            if (visible) ids.add(n.id);
            const isConnected = n.isConnectedSet && n.children && n.children.length;
            const activeChildId = isConnected
                ? (n.defaultMemberLayerId || (n.children[0] && n.children[0].id))
                : null;
            for (const c of n.children || [])
                recurse([c], visible && (!isConnected || c.id === activeChildId));
        }
    })(mapLayers(), true);
    return ids;
}

// Whether an element of the given kind / id on the given layer renders.
// While anything is isolated, ONLY the isolated element shows.
export function elementShown(kind, id, layerId, visIds) {
    if (typeof mapHost.isolatedId !== 'undefined' && mapHost.isolatedId)
        return mapHost.isolatedKind === kind && mapHost.isolatedId === id;
    if (!layerId) return true; // unassigned (legacy) → ignores the layer cascade
    return visIds.has(layerId);
}

// Terrain shapes → flat triangulated polygons on the ground. Each shape gets
// a tiny y offset by draw order so overlapping shapes (grass under forest)
// don't z-fight.
export function buildTerrain(root, visIds) {
    let order = 0;
    (function walk(nodes) {
        for (const n of nodes || []) {
            for (const sh of n.shapes || []) {
                if (!elementShown('shape', sh.id, n.id, visIds)) continue;
                const pts = sh.points || [];
                if (pts.length < 3) continue;
                const shape = new THREE.Shape();
                shape.moveTo(pts[0].x, pts[0].y);
                for (let i = 1; i < pts.length; i++) shape.lineTo(pts[i].x, pts[i].y);
                shape.closePath();
                const geo = new THREE.ShapeGeometry(shape);
                const mesh = new THREE.Mesh(geo, new THREE.MeshLambertMaterial({
                    color: toColor(sh.color, 0x6f8f4f),
                    side: THREE.DoubleSide,
                    // Ground stack: no depth writes + a strict renderOrder
                    // painter's stack. Sidesteps depth-buffer precision
                    // entirely, so nothing in the flat ground layer can
                    // z-fight at any distance.
                    depthWrite: false,
                }));
                // Shape is authored in XY; rotation.x = +90° lays it on XZ
                // with our (x, y_up, z=south) convention.
                mesh.rotation.x = Math.PI / 2;
                mesh.position.y = 0.02;
                mesh.renderOrder = RO_TERRAIN + order;
                mesh.receiveShadow = true;
                order++;
                root.add(mesh);
            }
            walk(n.children);
        }
    })(mapLayers());
}

// Base map images → flat textured ground quads, just below terrain.
function projectTexture(url) {
    const texture = new THREE.Texture();
    let disposed = false;
    texture.addEventListener('dispose', () => { disposed = true; });
    void projectImageUrl(url).then((resolved) => {
        if (!resolved || disposed) return;
        const image = new Image();
        image.onload = () => {
            if (disposed) return;
            texture.image = image;
            texture.needsUpdate = true;
        };
        image.src = resolved;
    }).catch((error) => {
        if (disposed) return;
        const canvas = document.querySelector('canvas');
        const message = error instanceof Error ? error.message : String(error);
        if (canvas) {
            canvas.title = message;
            canvas.setAttribute('aria-description', message);
            canvas.dispatchEvent(new CustomEvent('novalist-asset-error', { bubbles: true, detail: { message } }));
        }
    });
    return texture;
}

export function buildImages(root, visIds) {
    const base = (typeof mapHost.imageBaseUrl !== 'undefined' && mapHost.imageBaseUrl) || '';
    let order = 0;
    (function walk(nodes) {
        for (const n of nodes || []) {
            for (const img of n.images || []) {
                if (!elementShown('image', img.id, n.id, visIds)) continue;
                const w = img.width || 1, h = img.height || 1;
                const encoded = String(img.path || '')
                    .split('/').map(encodeURIComponent).join('/');
                const tex = projectTexture(base + encoded);
                tex.colorSpace = THREE.SRGBColorSpace;
                const mesh = new THREE.Mesh(
                    new THREE.PlaneGeometry(w, h),
                    new THREE.MeshBasicMaterial({
                        map: tex, side: THREE.DoubleSide, depthWrite: false,
                    })
                );
                mesh.renderOrder = RO_IMAGE + order;
                order++;
                // Lay flat then yaw about world Y by the image's 2D rotation.
                mesh.rotation.order = 'YXZ';
                mesh.rotation.x = Math.PI / 2;
                mesh.rotation.y = -(img.rotation || 0) * DEG;
                mesh.position.set((img.x || 0) + w / 2, -0.05, (img.y || 0) + h / 2);
                root.add(mesh);
            }
            walk(n.children);
        }
    })(mapLayers());
}

// Map-wide pins & labels (flat mapData.pins / mapData.labels, layer-bound by
// their layerId). Pins sit on the ground; labels float just above it.
export function buildMapAnnotations(root, visIds) {
    for (const p of (mapHost.mapData.pins || [])) {
        if (!elementShown('pin', p.id, p.layerId || '', visIds)) continue;
        const m = makePinMarker(p.color, p.label);
        m.position.set(p.x, 0, p.y);
        root.add(m);
    }
    for (const l of (mapHost.mapData.labels || [])) {
        if (!l.text) continue;
        if (!elementShown('label', l.id, l.layerId || '', visIds)) continue;
        const spr = makeLabelSprite(l.text, l.color, l.fontSize);
        spr.position.set(l.x, 1, l.y);
        root.add(spr);
    }
}
