import { mapState } from './map-state.js';

export function initializeMapProfiles() {
    mapState.TERRAIN_PRESETS = {
        grass:    '#8db360',
        forest:   '#4a7c3a',
        concrete: '#9a9a98',
        sand:     '#e3d2a0',
        hills:    '#a6b56a',
        mountain: '#8a7f70',
        water:    '#5b86b3',
    };
    mapState.SPLINE_PROFILES = {
        road: {
            motorway: { defaultWidth: 44, casing: { color: '#3f3413', extra: 5 },
                bands: [{ from: -1, to: 1, color: '#F7C45F' }],
                markings: [{ offset: 0, color: '#ffffff', width: 2, dash: [16, 12] },
                           { offset: -0.93, color: '#ffffff', width: 1.5 },
                           { offset: 0.93, color: '#ffffff', width: 1.5 }] },
            primary: { defaultWidth: 32, casing: { color: '#6b5a1a', extra: 4 },
                bands: [{ from: -1, to: 1, color: '#FFF2AF' }],
                markings: [{ offset: 0, color: '#e0b000', width: 1.8 },
                           { offset: -0.9, color: '#ffffff', width: 1.3 },
                           { offset: 0.9, color: '#ffffff', width: 1.3 }] },
            secondary: { defaultWidth: 24, casing: { color: '#7a7a7a', extra: 3 },
                bands: [{ from: -1, to: 1, color: '#FFFFFF' }],
                markings: [{ offset: 0, color: '#cfcfcf', width: 1.4, dash: [10, 10] }] },
            residential: { defaultWidth: 16, casing: { color: '#8a8a8a', extra: 2 },
                bands: [{ from: -1, to: 1, color: '#FFFFFF' }], markings: [] },
            service: { defaultWidth: 9, casing: { color: '#6e6e6e', extra: 1 },
                bands: [{ from: -1, to: 1, color: '#cfcfcf' }], markings: [] },
            pedestrian: { defaultWidth: 14, casing: { color: '#9a8d7a', extra: 1 },
                bands: [{ from: -1, to: 1, color: '#d8cbb4' }], markings: [] },
            trail: { defaultWidth: 12, casing: { color: '#d8cdb0', extra: 0 },
                bands: [{ from: -1, to: 1, color: '#d8cdb0' }],
                // A single dashed centre marking = the dotted footpath look.
                markings: [{ offset: 0, color: '#7a5c3a', width: 5, dash: [4, 10] }] },
            track: { defaultWidth: 26, casing: { color: '#3a2f22', extra: 1 },
                bands: [{ from: -1, to: 1, color: '#b9ad97' }], // ballast
                // Cross-ties: a thick dashed marking → rectangles `width` wide
                // (perpendicular) and `dash[0]` long, spaced by `dash[1]`. Then the
                // two rails on top. Tie offset is slightly off 0 so it counts as an
                // edge marking (drawn before / under the rails, not a centreline).
                markings: [{ offset: 0.001, color: '#5a4632', width: 30, dash: [4, 9] },
                           { offset: -0.55, color: '#4b4b4b', width: 2.2 },
                           { offset: 0.55, color: '#4b4b4b', width: 2.2 }] },
        },
        river: {
            brook: { defaultWidth: 8, casing: { color: '#80A7C2', extra: 1.5 },
                bands: [{ from: -1, to: 1, color: '#AADAFF' }], markings: [] },
            stream: { defaultWidth: 16, casing: { color: '#80A7C2', extra: 2 },
                bands: [{ from: -1, to: 1, color: '#AADAFF' }], markings: [] },
            river: { defaultWidth: 38, casing: { color: '#6f9ab8', extra: 3 },
                bands: [{ from: -1, to: 1, color: '#81B3D5' }], markings: [] },
            canal: { defaultWidth: 22, casing: { color: '#5d7d92', extra: 2 },
                bands: [{ from: -1, to: 1, color: '#81B3D5' }], markings: [], straight: true },
            estuary: { defaultWidth: 84, casing: { color: '#7a8b76', extra: 4 },
                bands: [{ from: -1, to: 1, color: '#9fb8c9' }], markings: [] },
        },
    };
}
