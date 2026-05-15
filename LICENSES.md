# Third-Party Asset and Code Licenses

Novalist itself is licensed under the MIT License — see [LICENSE](LICENSE) for
the project copyright.

The vegetation assets and the techniques they exercise are derived from the
open-source project [revo-realms](https://github.com/alezen9/revo-realms) by
Aleksandar Gjoreski. That project is also distributed under the MIT License,
which is reproduced below per the redistribution requirement.

## revo-realms (MIT)

The following files in this repository originate from or are derived from
revo-realms, branch `feat/new-world`:

- `Novalist.Desktop/Assets/Map/vegetation/sekai.glb`
  — sourced from `public/models/sekai.glb`. We use only the `pine_tree_bark`
  and `pine_tree_canopy` meshes from this GLB; the rest of the model is
  unused at runtime but present in the binary for simplicity.
- `Novalist.Desktop/Assets/Map/vegetation/realm.glb`
  — sourced from `public/models/realm.glb`. Currently unused at runtime but
  kept in case we later wire up the maple-tree variant from `Tree.ts`.
- `Novalist.Desktop/Assets/Map/vegetation/pine-canopy-diffuse.png`
  — sourced from `public/textures/new-world/pine-tree/diffuse.png`.
- `Novalist.Desktop/Assets/Map/vegetation/pine-canopy-diffuse.ktx2`
  — sourced from `public/textures/new-world/pine-tree/diffuse_2k.ktx2`.
  Preferred at runtime over the PNG; the PNG is the fallback.
- `Novalist.Desktop/Assets/Map/vegetation/tree-bark-diffuse.ktx2`
  — sourced from `public/textures/new-world/tree/bark_diffuse_512_uastc.ktx2`.
- `Novalist.Desktop/Assets/Map/vegetation/tree-bark-normal.ktx2`
  — sourced from `public/textures/new-world/tree/bark_normal_512_uastc.ktx2`.

The grass blade material and the pine tree material in
`Novalist.Desktop/Assets/Map/map3d.js` are adapted from `Grass.ts` and
`PineTrees.ts` in the same repository.

```
MIT License

Copyright (c) 2025 Aleksandar Gjoreski

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## three.js (MIT)

The 3D map view uses three.js (https://threejs.org/). three.js is distributed
under the MIT License — the relevant copyright header is preserved in the
bundled `Novalist.Desktop/Assets/Map/three.*.min.js` and
`GLTFLoader.js` files.
