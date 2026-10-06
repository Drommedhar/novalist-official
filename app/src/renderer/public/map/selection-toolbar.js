import { mapState } from './map-state.js';
import { findPinById, findLabelById } from './building-editing.js';
import { bbText, bbColor, bbSelect, bbLayerSelect, bbNumber, bbToggleGroup, bbButton, bbKbd } from './map-controls.js';
import { bbEntityPicker } from './entity-picker.js';
import { findImageById } from './image-api.js';
import { enterClipEdit } from './image-clipping.js';

export function renderSelectedBar() {
    const kind = mapState.selected.kind;
    mapState.bb.el.style.borderTopColor = mapState.MODE_ACCENT[kind] || mapState.MODE_ACCENT.idle;
    mapState.bb.left.textContent = ({ pin: mapState.mapStrings.bbPinLabel || 'Pin',
                             label: mapState.mapStrings.bbLabelSelLabel || 'Text label',
                             image: mapState.mapStrings.bbImageLabel || 'Image' })[kind] || kind;
    if (kind === 'pin') {
        const pin = findPinById(mapState.selected.id);
        if (pin) {
            mapState.bb.center.appendChild(bbText(pin.label || '',
                v => window.setPinLabel(mapState.selected.id, v),
                mapState.mapStrings.pinLabel, mapState.mapStrings.pinLabelWatermark));
            mapState.bb.center.appendChild(bbEntityPicker(pin.entityId || '', pin.entityType || '',
                (id, type) => window.setPinEntity(mapState.selected.id, id, type),
                mapState.mapStrings.pinEntity));
            mapState.bb.center.appendChild(bbColor(pin.color || '#f9c46a',
                hex => window.setPinColor(mapState.selected.id, hex),
                mapState.mapStrings.pinColor));
            // A dot for every settlement, ruin and pass meant telling a capital
            // from a campsite required reading every label. The shapes have
            // been on the model since maps shipped with nothing drawing them.
            mapState.bb.center.appendChild(bbSelect(
                pin.style === 'svg' ? (pin.iconPath || '') : '',
                [['', mapState.mapStrings.pinIconDot || 'Dot'],
                 ...Object.keys(mapState.PIN_ICONS).map(
                     name => [name, (mapState.mapStrings['pinIcon_' + name]) || name])],
                name => window.setPinIcon(mapState.selected.id, name),
                mapState.mapStrings.pinIcon || 'Shape'));
            // A pin that opens another map: a world map marks a city and the
            // city has its own map, which is the one relationship maps have and
            // the one Novalist could not express.
            if (mapState.otherMaps.length > 0) {
                mapState.bb.center.appendChild(bbSelect(
                    pin.targetMapId || '',
                    [['', mapState.mapStrings.pinNoTargetMap || 'No map'],
                     ...mapState.otherMaps.map(m => [m.id, m.name])],
                    id => window.setPinTargetMap(mapState.selected.id, id),
                    mapState.mapStrings.pinTargetMap || 'Opens map'));
            }
            const ls = bbLayerSelect('pin', pin.id, pin.layerId || '');
            if (ls) mapState.bb.center.appendChild(ls);
        }
    } else if (kind === 'label') {
        const lbl = findLabelById(mapState.selected.id);
        if (lbl) {
            mapState.bb.center.appendChild(bbNumber(lbl.fontSize || 18, 4, 400, 2,
                v => window.setLabelFontSize(mapState.selected.id, v), mapState.mapStrings.labelFontSize));
            mapState.bb.center.appendChild(bbColor(lbl.color || '#ffffff',
                hex => window.setLabelColor(mapState.selected.id, hex), mapState.mapStrings.labelColor));
            mapState.bb.center.appendChild(bbToggleGroup(lbl.align || 'center',
                [['left', mapState.mapStrings.alignLeft || 'L'],
                 ['center', mapState.mapStrings.alignCenter || 'C'],
                 ['right', mapState.mapStrings.alignRight || 'R']],
                v => window.setLabelAlign(mapState.selected.id, v)));
            const ls = bbLayerSelect('label', lbl.id, lbl.layerId || '');
            if (ls) mapState.bb.center.appendChild(ls);
        }
    } else if (kind === 'image') {
        const img = findImageById(mapState.selected.id);
        if (img) {
            mapState.bb.center.appendChild(bbNumber(img.minZoom || 0, 0, 100, 0.1,
                v => window.setElementZoomRange('image', mapState.selected.id, v, img.maxZoom || 0),
                mapState.mapStrings.imageMinZoom || 'Min zoom'));
            mapState.bb.center.appendChild(bbNumber(img.maxZoom || 0, 0, 100, 0.1,
                v => window.setElementZoomRange('image', mapState.selected.id, img.minZoom || 0, v),
                mapState.mapStrings.imageMaxZoom || 'Max zoom'));
            mapState.bb.center.appendChild(bbButton(mapState.mapStrings.clip || 'Edit clip',
                () => enterClipEdit(mapState.selected.layerId, mapState.selected.id)));
            if (img.clipPolygon && img.clipPolygon.length >= 3) {
                mapState.bb.center.appendChild(bbButton(mapState.mapStrings.clipClear || 'Clear clip',
                    () => window.setImageClip(mapState.selected.id, null), { danger: true }));
            }
            const ls = bbLayerSelect('image', img.id, mapState.selected.layerId || '');
            if (ls) mapState.bb.center.appendChild(ls);
        }
    }
    mapState.bb.right.appendChild(bbKbd(mapState.mapStrings.kbdDeleteRemoveEscDeselect || 'Delete remove · Esc deselect'));
    mapState.bb.right.appendChild(bbButton(mapState.mapStrings.ctxDelete, window.deleteSelected, { danger: true }));
}
