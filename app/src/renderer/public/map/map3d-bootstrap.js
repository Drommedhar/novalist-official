// 3D scene lifecycle and the public Map3D bridge. Scene resources are owned
// by the domain state modules and released by lifecycle.tearDown().

import * as THREE from "./three.webgpu.min.js";
import { keys, sceneState } from './map3d/scene-state.js';
import { buildSky, updateSunShadow } from './map3d/sky.js';
import { BOOST, LOOK_SPEED, MOVE_SPEED, WORLD_SCALE } from './map3d/scene-settings.js';
import { mapBounds } from './map3d/map-content.js';
import { grassDebug, grassState } from './map3d/grass-state.js';
import { GRASS_DEBUG, GRASS_MAX_BLADES_PER_TILE } from './map3d/grass-settings.js';
import { TREE_ASSET_TIMEOUT_MS, TREE_STATIC_MODE } from './map3d/tree-settings.js';
import { treeState } from './map3d/tree-state.js';
import { updateGrassVisibility } from './map3d/grass-tiles.js';
import { updateTreeVisibility } from './map3d/tree-tiles.js';
import { mapHost } from './map3d/host.js';
import { loadTreeAssets } from './map3d/tree-assets.js';
import { buildScene } from './map3d/scene.js';
import { tearDown } from './map3d/lifecycle.js';

// Whether this WebView exposes WebGPU at all.
export function webgpuSupported() {
    return typeof navigator !== 'undefined' && !!navigator.gpu;
}

// Full-screen message shown when WebGPU is unavailable / fails to init —
// beats a silent black canvas. Created lazily, reused.
export function showFallbackMessage() {
    if (!sceneState.fallbackEl) {
        const el = document.createElement('div');
        el.id = 'map3dFallback';
        el.textContent = '3D map needs WebGPU, which is not available in '
            + 'this WebView. Update the WebView2 runtime, or keep using the '
            + '2D map.';
        el.style.cssText = 'position:fixed;inset:0;display:flex;'
            + 'align-items:center;justify-content:center;text-align:center;'
            + 'padding:48px;box-sizing:border-box;background:#1c2530;'
            + 'color:#e8e8e8;font:14px "Segoe UI",sans-serif;z-index:99999;';
        document.body.appendChild(el);
        sceneState.fallbackEl = el;
    }
    sceneState.fallbackEl.style.display = 'flex';
}

export function hideFallbackMessage() {
    if (sceneState.fallbackEl) sceneState.fallbackEl.style.display = 'none';
}

// Create the WebGPU renderer + scene + lights on first enter. Async — the
// WebGPU device is acquired asynchronously. Returns true once ready, false
// if WebGPU is unavailable (a fallback message is shown in that case).
export async function ensureRenderer() {
    if (sceneState.rendererReady) return true;
    if (sceneState.renderer) return false; // a previous init attempt already failed
    sceneState.canvas = document.getElementById('stage3d');
    if (!webgpuSupported()) {
        console.error('[Map3D] WebGPU not available in this WebView');
        showFallbackMessage();
        return false;
    }
    try {
        sceneState.renderer = new THREE.WebGPURenderer({ canvas: sceneState.canvas, antialias: true });
        sceneState.renderer.setPixelRatio(window.devicePixelRatio || 1);
        sceneState.renderer.shadowMap.enabled = true;
        sceneState.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
        sceneState.renderer.toneMapping = THREE.ACESFilmicToneMapping;
        sceneState.renderer.toneMappingExposure = 1.0;
        await sceneState.renderer.init();
    } catch (e) {
        console.error('[Map3D] WebGPU init failed', e);
        sceneState.renderer = null;
        showFallbackMessage();
        return false;
    }
    sceneState.scene = new THREE.Scene();
    // No solid background colour — the SkyMesh below fills the framebuffer.
    // Kept as a fallback tint in case sky setup throws.
    sceneState.scene.background = new THREE.Color(0x9ec7e0);
    sceneState.camera = new THREE.PerspectiveCamera(60, 1, 1, 20000);
    // Light intensities are tuned for r184's physically-correct lighting +
    // ACES tone mapping — much higher numbers than the old WebGL pipeline.
    sceneState.scene.add(new THREE.AmbientLight(0xffffff, 1.4));
    // Directional light from the upper-left — matches the 2D roof-shading
    // light vector so 3D and 2D read consistently. Casts shadows; its
    // position + shadow frustum are fitted to the map in updateSunShadow().
    sceneState.sun = new THREE.DirectionalLight(0xffffff, 2.8);
    sceneState.sun.castShadow = true;
    sceneState.sun.shadow.mapSize.set(4096, 4096);
    sceneState.scene.add(sceneState.sun);
    sceneState.scene.add(sceneState.sun.target);
    // Force the shadow map render target to materialise before any water
    // is built — makeWater binds `sun.shadow.map.texture` into the water
    // material at construction time, so it has to exist by then.
    updateSunShadow();
    try { sceneState.renderer.render(sceneState.scene, sceneState.camera); } catch (error) { console.warn("map3d: ensureRenderer failed", error instanceof Error ? error.name : typeof error); }
    try {
        buildSky();
    } catch (e) {
        console.error('[Map3D] Sky setup failed — falling back to solid bg', e);
    }
    wireInput();
    sceneState.rendererReady = true;
    return true;
}

export function resize() {
    if (!sceneState.renderer) return;
    const w = window.innerWidth, h = window.innerHeight;
    sceneState.renderer.setSize(w, h, false);
    sceneState.camera.aspect = w / h || 1;
    sceneState.camera.updateProjectionMatrix();
}

// Skip WASD / Q / E when focus is in a typeable field (e.g. lil-gui number
// input). Without this the camera moves while you type slider values.
export function isTypingTarget(el) {
    if (!el) return false;
    const tag = el.tagName;
    return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable;
}

export function wireInput() {
    if (sceneState.inputWired) return;
    sceneState.inputWired = true;
    sceneState.canvas.addEventListener('mousedown', function () {
        if (sceneState.active) sceneState.canvas.requestPointerLock();
    });
    document.addEventListener('mousemove', function (e) {
        if (!sceneState.active || document.pointerLockElement !== sceneState.canvas) return;
        sceneState.yaw -= e.movementX * LOOK_SPEED;
        sceneState.pitch -= e.movementY * LOOK_SPEED;
        const lim = Math.PI / 2 - 0.05;
        sceneState.pitch = Math.max(-lim, Math.min(lim, sceneState.pitch));
    });
    window.addEventListener('keydown', function (e) {
        if (!sceneState.active) return;
        // Ignore keystrokes that the user is typing into the lil-gui sky
        // panel — without this, hitting "a" in a number field strafes the
        // camera and the value never reaches the input.
        if (isTypingTarget(e.target)) return;
        keys[e.code] = true;
        // Esc releases pointer lock (browser already does this); swallow so
        // it doesn't bubble to the 2D editor's Esc handlers.
        if (e.code === 'Escape') e.stopPropagation();
    });
    window.addEventListener('keyup', function (e) {
        if (isTypingTarget(e.target)) return;
        keys[e.code] = false;
    });
    window.addEventListener('resize', function () { if (sceneState.active) resize(); });
}

// Camera forward vector from yaw / pitch. yaw rotates around +y; x = sin,
// z = cos so frameCamera()'s atan2(x, z) stays consistent.
export function camDir() {
    return new THREE.Vector3(
        Math.sin(sceneState.yaw) * Math.cos(sceneState.pitch),
        Math.sin(sceneState.pitch),
        Math.cos(sceneState.yaw) * Math.cos(sceneState.pitch)
    );
}

export function frameCamera() {
    const b = mapBounds();
    // mapBounds is in unscaled map units; the scene is scaled by WORLD_SCALE.
    const s = WORLD_SCALE;
    const cx = (b.minX + b.maxX) / 2 * s, cz = (b.minY + b.maxY) / 2 * s;
    const span = Math.max(600, b.maxX - b.minX, b.maxY - b.minY) * s;
    sceneState.camera.position.set(cx, span * 0.8, cz + span * 0.9);
    const to = new THREE.Vector3(cx, 0, cz).sub(sceneState.camera.position).normalize();
    sceneState.pitch = Math.asin(to.y);
    sceneState.yaw = Math.atan2(to.x, to.z);
}

// Refresh the top-left 3D HUD (FPS + grass GPU memory estimate + pool /
// cache stats + JS heap). Throttled to ~5 Hz so text doesn't strobe.
export function updateHud3d(now) {
    if (!sceneState.hud3dEl) return;
    if (now - sceneState.hudLastUpdate < 200) return;
    sceneState.hudLastUpdate = now;
    // Per-instance buffer cost: 16-float instance matrix + grassRoot (2)
    // + grassTint (3) + grassPhase (1) at 4 bytes each.
    const bytesPerInstance = (16 + 2 + 3 + 1) * 4;
    const grassBytes = grassState.grassPool.length
        * GRASS_MAX_BLADES_PER_TILE * bytesPerInstance;
    const grassMB = grassBytes / (1024 * 1024);
    const poolUsed = grassState.grassPool.length - grassState.grassFreeSlots.length;
    let drawnTiles = 0;
    for (const tile of grassState.grassTileCache.values()) {
        if (tile && tile.visible && tile.count > 0) drawnTiles++;
    }
    const billboardCount = grassState.grassBillboardMesh ? grassState.grassBillboardMesh.count : 0;
    const lines = [
        'FPS: ' + sceneState.hudFps.toFixed(0),
        'Grass VRAM (est): ' + grassMB.toFixed(0) + ' MB',
        'Grass: ' + poolUsed + '/' + grassState.grassPool.length
            + ' (' + drawnTiles + ' blade, ' + billboardCount + ' billboard)',
    ];
    if (TREE_STATIC_MODE) {
        const treeCount = treeState.treeBarkStaticMesh ? treeState.treeBarkStaticMesh.count : 0;
        lines.push('Trees: ' + treeCount + ' (static)');
    } else {
        let drawnTreeTiles = 0;
        for (const t of treeState.treeTileCache.values()) {
            if (t && t.visible && t.count > 0) drawnTreeTiles++;
        }
        const treePoolUsed = treeState.treePool.length - treeState.treeFreeSlots.length;
        const treeBbCount = treeState.treeBillboardMesh ? treeState.treeBillboardMesh.count : 0;
        lines.push('Trees: ' + treePoolUsed + '/' + treeState.treePool.length
            + ' (' + drawnTreeTiles + ' tile, ' + treeBbCount + ' billboard)');
    }
    const perf = (typeof performance !== 'undefined') ? performance : null;
    if (perf && perf.memory) {
        const heapMB = perf.memory.usedJSHeapSize / (1024 * 1024);
        lines.push('JS heap: ' + heapMB.toFixed(0) + ' MB');
    }
    sceneState.hud3dEl.textContent = lines.join('\n');
}

export function tick(t) {
    if (!sceneState.active) return;
    sceneState.rafId = requestAnimationFrame(tick);
    const dt = Math.min(0.05, (t - sceneState.lastT) / 1000 || 0);
    sceneState.lastT = t;
    // EMA-smoothed FPS for the HUD.
    if (dt > 0) sceneState.hudFps = sceneState.hudFps * 0.9 + (1 / dt) * 0.1;
    const fwd = camDir();
    const right = new THREE.Vector3()
        .crossVectors(fwd, new THREE.Vector3(0, 1, 0)).normalize();
    const move = new THREE.Vector3();
    if (keys['KeyW']) move.add(fwd);
    if (keys['KeyS']) move.sub(fwd);
    if (keys['KeyD']) move.add(right);
    if (keys['KeyA']) move.sub(right);
    if (keys['KeyE']) move.y += 1;
    if (keys['KeyQ']) move.y -= 1;
    if (move.lengthSq() > 0) {
        const sp = MOVE_SPEED * WORLD_SCALE * dt
            * ((keys['ShiftLeft'] || keys['ShiftRight']) ? BOOST : 1);
        sceneState.camera.position.addScaledVector(move.normalize(), sp);
    }
    // Keep the camera above the ground. Looking down and pressing W would
    // otherwise push the camera below Y=0; from underground every blade,
    // building, and the backdrop renders from the wrong side and the
    // scene visually "stops moving" until the user pitches back up.
    if (sceneState.camera.position.y < 1) sceneState.camera.position.y = 1;
    sceneState.camera.lookAt(sceneState.camera.position.clone().add(camDir()));
    updateSunShadow(); // shadow frustum tracks the camera
    updateGrassVisibility(); // hide grass tiles outside the render radius
    updateTreeVisibility();
    updateHud3d(t);
    // WaterMesh advances its own flow animation via the renderer's
    // updateBefore hook — no manual time stepping needed here.
    const tRender0 = GRASS_DEBUG ? performance.now() : 0;
    sceneState.renderer.render(sceneState.scene, sceneState.camera);
    if (GRASS_DEBUG) {
        const renderDt = performance.now() - tRender0;
        grassDebug.renderMs += renderDt;
        grassDebug.renderFrames++;
        if (renderDt > grassDebug.renderMaxMs) grassDebug.renderMaxMs = renderDt;
        // Capture per-frame GPU draw count BEFORE three.js auto-resets it
        // on the next render — but for clarity also fold this into the
        // running max-per-frame so the per-second log can show worst case.
        const info = sceneState.renderer.info && sceneState.renderer.info.render;
        if (info) {
            if (info.calls > grassDebug.renderMaxCalls) grassDebug.renderMaxCalls = info.calls;
        }
    }
}

window.Map3D = {
    isActive: function () { return sceneState.active; },
    enter: async function () {
        const ok = await ensureRenderer();
        if (!ok) {
            // WebGPU unavailable — still swap to the (fallback-covered) 3D
            // canvas so the message shows, but don't start a render loop.
            if (sceneState.canvas) sceneState.canvas.style.display = 'block';
            if (typeof mapHost.stage !== 'undefined') mapHost.stage.style.display = 'none';
            if (typeof mapHost.hud !== 'undefined' && mapHost.hud) mapHost.hud.style.display = 'none';
            try { mapHost.sendMessage({ type: 'map3dEntered' }); } catch (error) { console.warn("map3d: operation failed", error instanceof Error ? error.name : typeof error); }
            return;
        }
        hideFallbackMessage();
        sceneState.active = true;
        sceneState.canvas.style.display = 'block';
        if (typeof mapHost.stage !== 'undefined') mapHost.stage.style.display = 'none';
        if (typeof mapHost.hud !== 'undefined' && mapHost.hud) mapHost.hud.style.display = 'none';
        if (!sceneState.hud3dEl) sceneState.hud3dEl = document.getElementById('hud3d');
        if (sceneState.hud3dEl) sceneState.hud3dEl.style.display = 'block';
        resize();
        // Wrap the whole entry sequence so any thrown error surfaces in
        // the host C# console instead of leaving a black canvas.
        try {
            mapHost.log('[Map3D.enter] start');
            try { mapHost.sendMessage({ type: 'map3dStep', step: 'before-build' }); } catch (error) { console.warn("map3d: operation failed", error instanceof Error ? error.name : typeof error); }
            mapHost.log('[Map3D.enter] loading tree assets...');
            // Trees are optional - the scene builds without them - so a
            // load that stalls must cost the writer its trees rather than
            // the whole view. Left unbounded it was a spinner that said
            // "Loading tree assets" and never said anything else again.
            const treesOk = await Promise.race([
                loadTreeAssets(),
                new Promise(function (resolve) {
                    setTimeout(function () {
                        mapHost.log('[tree] assets took too long; continuing without them');
                        resolve(false);
                    }, TREE_ASSET_TIMEOUT_MS);
                })
            ]);
            mapHost.log('[Map3D.enter] tree assets loaded: ' + treesOk);
            try { mapHost.sendMessage({ type: 'map3dStep', step: 'after-tree-assets' }); } catch (error) { console.warn("map3d: operation failed", error instanceof Error ? error.name : typeof error); }
            mapHost.log('[Map3D.enter] buildScene()...');
            buildScene();
            mapHost.log('[Map3D.enter] buildScene done');
            try { mapHost.sendMessage({ type: 'map3dStep', step: 'after-build' }); } catch (error) { console.warn("map3d: operation failed", error instanceof Error ? error.name : typeof error); }
            frameCamera();
            try { mapHost.sendMessage({ type: 'map3dStep', step: 'after-frame' }); } catch (error) { console.warn("map3d: operation failed", error instanceof Error ? error.name : typeof error); }
            sceneState.lastT = performance.now();
            sceneState.rafId = requestAnimationFrame(tick);
            mapHost.log('[Map3D.enter] tick scheduled');
            // The bottom bar builds itself from state and nothing else
            // tells it the state just changed, so it went on offering the
            // 2D editor's advice to somebody standing in a field. Guarded
            // on its own: a label failing to redraw must never be able to
            // reach the catch below and turn a working scene into a
            // failed entry.
            try {
                if (typeof mapHost.renderBottomBar === 'function') mapHost.renderBottomBar();
            } catch (error) { console.warn("map3d: operation failed", error instanceof Error ? error.name : typeof error); }
            try { mapHost.sendMessage({ type: 'map3dEntered' }); } catch (error) { console.warn("map3d: operation failed", error instanceof Error ? error.name : typeof error); }
        } catch (err) {
            const msg = err && err.message ? err.message : String(err);
            mapHost.log('[Map3D.enter] EXCEPTION: ' + (err && err.stack ? err.stack : err));
            sceneState.active = false;
            try { mapHost.sendMessage({ type: 'map3dError', message: msg }); } catch (error) { console.warn("map3d: operation failed", error instanceof Error ? error.name : typeof error); }
        }
    },
    exit: function () {
        sceneState.active = false;
        if (sceneState.canvas) sceneState.canvas.style.display = 'none';
        if (typeof mapHost.stage !== 'undefined') mapHost.stage.style.display = '';
        if (typeof mapHost.hud !== 'undefined' && mapHost.hud) mapHost.hud.style.display = '';
        if (sceneState.hud3dEl) sceneState.hud3dEl.style.display = 'none';
        // Full teardown: dispose renderer / scene / textures so the next
        // enter() rebuilds from scratch. Re-using a stale WebGPU device
        // after the canvas was hidden caused a stale-frame + stall on
        // reopen.
        tearDown();
        try {
            if (typeof mapHost.renderBottomBar === 'function') mapHost.renderBottomBar();
        } catch (error) { console.warn("map3d: operation failed", error instanceof Error ? error.name : typeof error); }
        try { mapHost.sendMessage({ type: 'map3dExited' }); } catch (error) { console.warn("map3d: operation failed", error instanceof Error ? error.name : typeof error); }
    },
    // Full rebuild — called by the host / map.html on mapChanged while 3D
    // is active. Phase 1 only rebuilds the ground plane.
    rebuild: function () {
        if (!sceneState.active || !sceneState.renderer) return;
        buildScene();
    },
};
