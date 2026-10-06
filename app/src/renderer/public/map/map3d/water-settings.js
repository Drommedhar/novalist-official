// Water bodies don't carve real geometry — the depth is faked in the water
// shader. Each water-surface vertex carries a baked `waterDepth` (0 at the
// shore, 1 in the interior); the shader darkens by it and parallax-shifts
// the refracted scene below by it, so the bed reads as a recessed basin.
export const WATER_SURFACE_Y = 0.55; // water plane — just above terrain / roads

// Scales the baked waterDepth attribute (0..1) before it's used in the
// shader. > 1 stretches "deep" so that more of the lake reads as deep.
export const BASIN_DEPTH = 1.2;

// Water shader knobs (kept in sync with water-demo/main.js)
export const WATER_FALLBACK_COLOR = 0x4a7a9a;

export const WATER_DEEP_MULT = 0.2;

export const WATER_DEEP_TINT = 1.0;

export const WATER_RIPPLE = 15;

export const WATER_SHINE = 0.5;

export const WATER_NORMAL_SCALE = 3;

export const WATER_NORMAL_SCALE2 = 1.7;

export const WATER_REFLECT_DISTORT = 0.8;

export const WATER_FOAM_WIDTH = 0.022;

export const WATER_FOAM_SCALE = 60;

export const WATER_FOAM_DRIFT = 0.012;

export const WATER_FOAM_SPEED = 1.6;

export const WATER_FOAM_COLOR = 0xe6eef0;

export const WATER_SCATTER = 0.0;

export const WATER_SCATTER_POWER = 24;

export const WATER_SCATTER_COLOR = 0xd8eccd;

export const WATER_CAUSTIC = 10.8;

export const WATER_CAUSTIC_SCALE = 0.1;

export const WATER_CAUSTIC_SPEED = 0.04;

export const WATER_CAUSTIC_SHARP = 1.4;

export const WATER_CAUSTIC_COLOR = 0xfff5d0;

export const WATER_CAUSTIC_PARALLAX = 0.6; // how much the bed shifts under view (with depth)

export const WATER_CAUSTIC_TINT = 1.0; // how strongly caustics take the water's colour at depth

export const WATER_CAUSTIC_MIN_SCALE = 1.0; // world-space tile size of the caustic pattern (uniform across depth)

export const WATER_CAUSTIC_DEPTH_SCALE = 1.0; // depth fade exponent: 0 = caustics everywhere, 1 = linear, 2 = squared (shallow-biased), 4+ = thin rim only

// World-space drop from water surface to the virtual bed used for the
// shadow-bend (sun-tilt projects bedPos to a different shadow UV than the
// surface, so building shadows bend into deeper water).
export const WATER_BED_DEPTH = 15;

// Shadow strength on the water (0 = no shadow visible at all, 1 = full).
// Affects BOTH the base-water darkening AND the caustic kill — so dialling
// it down hides the shadow from every channel at once.
export const WATER_SHADOW_DARKEN = 0.25;

// Depth fade exponent on shadow strength. 0 = shadow uniform at every
// depth, 1 = linear fade (full at shore, none at full depth), 2+ = sharper.
// Deep water tints itself so dark that a shadow on top reads wrong; this
// fades the shadow back out at depth.
export const WATER_SHADOW_DEPTH_FADE = 2.0;

// Wave displacement (vertex tessellation + sin-sum waves)
// Wave height in WORLD UNITS at the shore (d=0) and at full depth (d=1).
// The shader sums three travelling sin waves at varying frequencies and
// scales their amplitude by mix(SHALLOW, DEEP, dRaw) so banks ripple
// gently and the open water rolls. 0 disables.
export const WATER_WAVE_SHALLOW = 0.01;

export const WATER_WAVE_DEEP = 0.4;

// How fast the wave field travels (world-units / s of the time term).
export const WATER_WAVE_SPEED = 0.9;

// Mesh density: levels of midpoint subdivision applied to the lake's
// initial triangulation. 5 = 4^5 = 1024 sub-triangles per original (dense
// enough that the sin-sum waves don't read as facets). LOD steps down for
// distant or huge lakes.
export const WATER_LAKE_SUBDIV_BASE = 5;

export const WATER_LAKE_SUBDIV_FAR = 3;

// Distance (in world units) past which a lake drops one subdivision level.
// Cheap LOD: classify each lake at build time by camera distance.
export const WATER_LAKE_LOD_FAR = 600;

// Per-axis river ribbon subdivision multiplier. 1 keeps the raw 7-wide
// CROSS × N-long sampled rows; 2 doubles each axis (4x quads), 3 triples.
export const WATER_RIVER_SUBDIV = 5;

// Map-specific water geometry knobs (no equivalent in demo)
// Fraction of the way from shore to the deepest interior point at which the
// water reads fully deep. Lower = far more deep area, a thin shallow rim.
export const WATER_DEEP_REACH = 0.25;

export const WATER_FLOW = 5; // river flow strength — how far the ripples scroll downstream

export const WATER_TURB = 2; // curvature turbulence — cross-flow kick on river bends

export const WATER_FLOW_TAPER = 1.25; // bank-to-centre speed falloff exponent

// World units per normal-map tile. WaterMesh samples ripples from the
// geometry's uv (times `scale`), so the uv must carry a sane world scaling
// — without this, ShapeGeometry's default uv (raw world coords, hundreds of
// units) tiles the normal map into invisibility and the water reads glassy.
export const WATER_UV_TILE = 28;
