export const keys = {};

// Renderer resources are recreated on entry and cleared by tearDown. Input
// wiring survives that reset because the page installs its listeners once.
export const sceneState = {
    renderer: null,
    scene: null,
    camera: null,
    sceneRoot: null,
    sun: null,
    canvas: null,
    hud3dEl: null,
    hudFps: 60,
    hudLastUpdate: 0,
    active: false,
    rendererReady: false,
    inputWired: false,
    rafId: 0,
    lastT: 0,
    fallbackEl: null,
    yaw: 0,
    pitch: -0.5,
};
