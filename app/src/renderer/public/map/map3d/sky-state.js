import * as THREE from "../three.webgpu.min.js";

import {
    SKY_CLOUD_COVERAGE,
    SKY_CLOUD_DENSITY,
    SKY_CLOUD_ELEVATION,
    SKY_MIE_COEFFICIENT,
    SKY_MIE_DIRECTIONAL_G,
    SKY_RAYLEIGH,
    SKY_TURBIDITY,
    SUN_DIR,
} from './sky-settings.js';

// Default elevation / azimuth derived from the legacy SUN_DIR (~45° up,
// ~−123° azimuth). Keeps the lighting matching what the rest of the app
// was tuned against.
export const skyParams = {
    turbidity: SKY_TURBIDITY,
    rayleigh: SKY_RAYLEIGH,
    mieCoefficient: SKY_MIE_COEFFICIENT,
    mieDirectionalG: SKY_MIE_DIRECTIONAL_G,
    elevation: THREE.MathUtils.radToDeg(Math.asin(SUN_DIR.y)),
    azimuth: THREE.MathUtils.radToDeg(Math.atan2(SUN_DIR.z, SUN_DIR.x)),
    cloudCoverage: SKY_CLOUD_COVERAGE,
    cloudDensity: SKY_CLOUD_DENSITY,
    cloudElevation: SKY_CLOUD_ELEVATION,
    showSunDisc: true,
};

// The sky, environment target, GUI and shared sunlight uniform share the
// renderer's lifetime; skyParams retains the writer's settings between entries.
export const skyState = {
    sky: null,
    skyEnvRT: null,
    skyGui: null,
    sunDirUniform: null,
};
