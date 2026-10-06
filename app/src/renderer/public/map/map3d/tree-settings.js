// Long enough that a cold cache on a slow disk still gets its trees - the
// whole set decodes in a fifth of a second warm - and short enough that a
// load which is never going to finish does not hold the view for ever.
export const TREE_ASSET_TIMEOUT_MS = 20000;

// Procedural trees
// Same pool / tile / cache pattern as grass, but with a low-poly tree
// mesh (cylinder trunk + cone foliage) and a standing camera-facing
// billboard quad as the far-LOD substitute.
export const TREE_DENSITY = 0.00125; // trees per world unit² (raise = more dense forest)

export const TREE_SCALE = 7; // base scale multiplier on top of GLB normalisation

export const TREE_TILE_SIZE = 40;

export const TREE_NEAR_RADIUS = 1; // full density inside this

export const TREE_RENDER_RADIUS = 600; // density fades to 0 here

export const TREE_FALLOFF_POWER = 2;

export const TREE_BILLBOARD_DISTANCE = 200; // beyond this: standing billboard

export const TREE_BILLBOARD_CAPACITY = 2000;

export const TREE_BILLBOARD_TEX_RES = 256;

export const TREE_POOL_SIZE = 200;

export const TREE_MAX_PER_TILE = 128; // 40² × 0.05 = 80, +headroom

export const TREE_TRUNK_HEIGHT = 3;

export const TREE_TRUNK_RADIUS = 0.25;

export const TREE_FOLIAGE_HEIGHT = 4;

export const TREE_FOLIAGE_RADIUS = 2;

export const TREE_TRUNK_COLOR = 0x6b4423;

export const TREE_FOLIAGE_COLOR = 0x3c6b2a;

export const TREE_TICK_BUDGET_MS = 3;

// Trees are sparse enough (0.05/m²) that the entire forest fits in one
// InstancedMesh per material — no streaming, no LOD, no billboard. Set
// false to fall back to the pool / tile streaming code.
export const TREE_STATIC_MODE = true;
