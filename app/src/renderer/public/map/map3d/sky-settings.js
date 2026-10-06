// Sky (Preetham analytic atmosphere)
// Geometry size — has to wrap the camera, but the vertex node clamps depth
// to camera.far, so the actual scale only needs to keep the box outside the
// near plane and inside frustum culling. 50k is plenty for our world span.
export const SKY_SCALE = 50000;

export const SKY_TURBIDITY = 2;

export const SKY_RAYLEIGH = 1.2;

export const SKY_MIE_COEFFICIENT = 0.005;

export const SKY_MIE_DIRECTIONAL_G = 0.8;

export const SKY_CLOUD_COVERAGE = 0.45;

export const SKY_CLOUD_DENSITY = 0.4;

export const SKY_CLOUD_ELEVATION = 0.5;

// Unit direction the sunlight travels FROM (upper-left, slightly downward).
// Mutable — the in-editor sky GUI rewrites this from elevation / azimuth
// sliders. `updateSunShadow` and the SkyMesh `sunPosition` uniform both
// read it, so updating it here drives shadow + sky glow together.
export const SUN_DIR = (function () {
    const v = { x: -0.55, y: 1.0, z: -0.84 };
    const l = Math.hypot(v.x, v.y, v.z);
    return { x: v.x / l, y: v.y / l, z: v.z / l };
})();
