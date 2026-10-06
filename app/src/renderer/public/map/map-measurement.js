import { mapState } from './map-state.js';
import { sendMessage } from './map-messages.js';
import { findLayer } from './map-layers.js';

/** A round ground distance near the target, so the bar reads 50 km, not 47.3. */
export function niceDistance(target) {
    if (!(target > 0)) return 1;
    const magnitude = Math.pow(10, Math.floor(Math.log10(target)));
    for (const step of mapState.SCALE_STEPS) {
        if (step * magnitude >= target) return step * magnitude;
    }
    return 10 * magnitude;
}

export function updateScaleBar() {
    if (!mapState.scalebar) return;
    const scale = mapState.mapData.scale;
    if (!scale || !(scale.unitsPer > 0)) { mapState.scalebar.hidden = true; return; }

    // Aim for a bar about 120px wide, then round the distance it represents
    // and let the bar take whatever width that works out to.
    const worldUnits = 120 / Math.max(mapState.zoom, 0.0001);
    const ground = niceDistance(worldUnits * scale.unitsPer);
    const pixels = (ground / scale.unitsPer) * mapState.zoom;

    mapState.scalebar.hidden = false;
    mapState.scalebar.querySelector('.sb-bar').style.width = pixels.toFixed(0) + 'px';
    mapState.scalebar.querySelector('.sb-text').textContent =
        formatDistance(ground) + ' ' + (scale.unit || '');
}

/** Distances read as numbers a person says out loud, not as float noise. */
export function formatDistance(value) {
    if (value >= 100) return String(Math.round(value));
    if (value >= 10) return value.toFixed(1).replace(/\.0$/, '');
    return value.toFixed(2).replace(/0+$/, '').replace(/\.$/, '');
}

export function updateGrid() {
    if (!mapState.mapGrid) return;
    const spacing = mapState.mapData.scale && mapState.mapData.scale.gridSpacing;
    if (!(spacing > 0)) { mapState.mapGrid.style.backgroundImage = ''; return; }

    const step = spacing * mapState.zoom;
    // Under four pixels a grid is a grey wash, which is worse than no grid.
    if (step < 4) { mapState.mapGrid.style.backgroundImage = ''; return; }

    mapState.mapGrid.style.backgroundImage =
        'linear-gradient(to right, rgba(0,0,0,0.18) 1px, transparent 1px),' +
        'linear-gradient(to bottom, rgba(0,0,0,0.18) 1px, transparent 1px)';
    mapState.mapGrid.style.backgroundSize = step + 'px ' + step + 'px';
    // Offset by the pan so the lines stay on the same world coordinates rather
    // than sliding under the map as it moves.
    mapState.mapGrid.style.backgroundPosition =
        (((mapState.pan.x % step) + step) % step) + 'px ' + (((mapState.pan.y % step) + step) % step) + 'px';
}

export function rulerClick(worldX, worldY) {
    if (!mapState.rulerFrom) { mapState.rulerFrom = { x: worldX, y: worldY }; updateHud(); return; }
    const dx = worldX - mapState.rulerFrom.x;
    const dy = worldY - mapState.rulerFrom.y;
    const units = Math.sqrt((dx * dx) + (dy * dy));
    const scale = mapState.mapData.scale;
    sendMessage({
        type: 'measured',
        worldUnits: units,
        ground: scale && scale.unitsPer > 0 ? units * scale.unitsPer : 0,
        unit: (scale && scale.unit) || ''
    });
    mapState.rulerFrom = null;
    updateHud();
}

export function updateHud() {
    if (!mapState.hud) return;
    const parts = [];
    parts.push(mapState.mapData.name || mapState.mapStrings.hudMap || 'Map');
    parts.push(`${mapState.mapStrings.hudZoom || 'zoom'} ${mapState.zoom.toFixed(2)}`);
    parts.push(`(${mapState.lastCursorWorld.x.toFixed(0)}, ${mapState.lastCursorWorld.y.toFixed(0)})`);
    const layer = findLayer(mapState.activeLayerId);
    if (layer) parts.push(`${mapState.mapStrings.hudLayer || 'Layer'}: ${layer.name || layer.id}`);
    if (mapState.splineDraft)   parts.push(`${mapState.mapStrings.hudSplineDraft  || 'Spline draft'}: ${mapState.splineDraft.points.length} ${mapState.mapStrings.hudKnots || 'knots'}`);
    if (mapState.terrainDraft)  parts.push(`${mapState.mapStrings.hudTerrainDraft || 'Terrain draft'}: ${mapState.terrainDraft.points.length} ${mapState.mapStrings.hudVerts || 'verts'}`);
    if (mapState.borderDraft)   parts.push(`${mapState.mapStrings.hudBorderDraft  || 'Border draft'}: ${mapState.borderDraft.points.length} ${mapState.mapStrings.hudVerts || 'verts'}`);
    if (mapState.wallDraft)     parts.push(`${mapState.mapStrings.hudWallDraft    || 'Wall draft'}: ${mapState.wallDraft.points.length} ${mapState.mapStrings.hudPts || 'pts'}`);
    if (mapState.rulerFrom) parts.push(mapState.mapStrings.hudRuler || 'Ruler: click the far end');
    mapState.hud.textContent = parts.join(' · ');
    updateScaleBar();
    updateGrid();
}

// Reflect current toolMode in body class so CSS can drive cursor + hover affordances.
export function applyToolModeClass() {
    const cls = document.body.classList;
    ['tool-select', 'tool-add-pin', 'tool-add-label', 'tool-spline',
     'tool-terrain', 'tool-border', 'tool-building'].forEach(c => cls.remove(c));
    cls.add('tool-' + mapState.toolMode);
}

export function initializeMapMeasurement() {
    mapState.SCALE_STEPS = [1, 2, 5];
    mapState.rulerFrom = null;
    mapState.lastCursorWorld = { x: 0, y: 0 };
}
