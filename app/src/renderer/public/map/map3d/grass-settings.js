// Procedural grass
// Sampling is LAZY and tile-based around the camera. buildGrass() only
// records the grass polygons; tiles are sampled and added to the scene on
// demand from updateGrassVisibility(). A shader-side fade shrinks blade
// height to 0 near the cull radius so tile pop is invisible.
export const GRASS_DENSITY = 4.0; // blades per world unit² at full density

export const GRASS_TILE_SIZE = 20; // world units per cached tile

export const GRASS_NEAR_RADIUS = 1; // full density inside this from camera

export const GRASS_RENDER_RADIUS = 1200; // density fades to 0 at this radius

// Falloff shape between NEAR and RENDER. Higher = sharper drop.
//   2  → gentle quadratic
//   4  → recommended: noticeable steepness without a hard ring
//   8+ → tight ring of dense grass, nearly empty past mid-radius
export const GRASS_FALLOFF_POWER = 16;

// Beyond this distance from the camera, a tile drops the per-blade
// InstancedMesh and instead renders a flat textured quad (the texture is
// pre-rendered once from the actual blade shader). Cheap on GPU, lossless
// visually at the distances where individual blades don't read anyway.
export const GRASS_BILLBOARD_DISTANCE = 2000;

// Max simultaneous billboard tiles. Bounded by tiles in (BILLBOARD_DISTANCE
// .. RENDER_RADIUS) ring: ceil(π × (R² − B²) / TILE²) + headroom.
export const GRASS_BILLBOARD_CAPACITY = 0;

export const GRASS_BILLBOARD_TEX_RES = 512; // texture resolution per side

// Pool: pre-allocate N max-sized InstancedMesh objects on scene entry.
// sampleGrassTile reuses one of these by overwriting its instance buffers
// (typed-array .set + needsUpdate=true), avoiding the per-new-tile GPU
// resource allocation / bind-group setup that was costing ~30 ms PER NEW
// TILE inside renderer.render() in the previous design. When the pool is
// exhausted, the furthest-from-camera slot is evicted to make room.
export const GRASS_POOL_SIZE = 500; // covers RENDER_RADIUS with headroom

export const GRASS_MAX_BLADES_PER_TILE = 6400; // 40² × 1.28 — safe upper bound

// Sampling is bounded by time, not count. The prime tick on first entry
// builds every tile in the radius synchronously (one entry hitch is fine);
// every subsequent tick stops sampling new tiles once it has used this
// many ms, so movement-driven tile spawns can never spike a frame.
// Higher = backlog clears faster (less visible "missing tile" gaps when
// moving fast); lower = more FPS headroom. With pool reuse the per-sample
// cost is dominated by JS-side PiP tests, not GPU resource creation, so
// this can be generous.
export const GRASS_TICK_BUDGET_MS = 2;

export const GRASS_DEBUG = true; // dumps [grass] metrics to console once/sec

export const GRASS_BLADE_HEIGHT = 1.0;

export const GRASS_BLADE_WIDTH = 0.3;

export const GRASS_WIND_AMPL = 0.45;

export const GRASS_WIND_FREQ = 0.6;

export const GRASS_FALLBACK_COLOR = 0x6f8f4f;
