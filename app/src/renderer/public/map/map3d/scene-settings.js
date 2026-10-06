export const WORLD_SCALE = 1; // whole scene scaled uniformly (all axes + height)

export const FLOOR_H = 20; // world units per building floor (tunable — Phase 4)

export const IWALL_H = FLOOR_H - 0.5; // interior wall height — just under the ceiling slab

export const MOVE_SPEED = 120; // world units / second

export const LOOK_SPEED = 0.0022; // radians / pixel

export const BOOST = 8; // Shift multiplier

export const BACKDROP_Y = -2; // neutral backdrop ground plane

export const DEG = Math.PI / 180;

// renderOrder bands for the flat ground stack (all depthWrite:false, painted
// back-to-front). Buildings (Phase 4) render above this with normal depth.
export const RO_BACKDROP = 0;

export const RO_IMAGE = 100;

export const RO_TERRAIN = 1000;

export const RO_SPLINE = 5000; // casing band, fill band (+2000), water band (+4000)

export const RO_BUILDING = 20000; // 3D volumes — above the flat ground stack, normal depth

export const RO_BILLBOARD = 30000; // label sprites / pins — drawn over everything
