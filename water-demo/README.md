# Water shader demo

Standalone three.js WebGPU page that runs the same `WaterMesh` + colour-node
wrapper the in-app 3D map uses. Lets me iterate the shader (caustics, foam,
scatter, shadow) without rebuilding Avalonia each round.

Files in this folder:

- `index.html` — page shell, importmap, on-screen error overlay.
- `main.js` — scene, lights, four shadow-casting cubes, and one water plane
  with baked `waterDepth` (1 in centre, 0 at rim) + `flowDir`.

The page reuses the bundled three.js builds and `Water2Mesh.js` from
`../Novalist.Desktop/Assets/Map/` — no duplicates.

## Running

The page is an ES-module document, so a real HTTP origin is needed
(`file://` blocks module loading in Chromium).

From the repo root (`e:/git/novalist-official`):

```
python -m http.server 8000
```

or:

```
npx serve -l 8000 .
```

then open <http://localhost:8000/water-demo/index.html>.

Needs a browser with WebGPU enabled — recent Chrome / Edge (113+) on a
supported GPU. If WebGPU is missing, an error appears at the bottom of the
page; check `chrome://gpu`.

## Iteration loop

1. Edit `main.js`.
2. Refresh the browser.
3. DevTools console (F12) surfaces any compile / runtime errors.
4. Once a fix works, port the same change back to
   `../Novalist.Desktop/Assets/Map/map3d.js` so the in-app map benefits.

The shader knobs at the top of `main.js` are kept in sync with the
constants block in `map3d.js`; treat them as one set of values.
