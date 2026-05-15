#!/usr/bin/env node
// Tiny static server for the water demo. Serves repo root on :8000 so
// `water-demo/index.html` can pull files from `Novalist.Desktop/Assets/Map/`
// via relative paths.
//
// Run from the repo root:   node water-demo/serve.js
// then open http://localhost:8000/water-demo/index.html
const http = require('http');
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const PORT = Number(process.env.PORT || 8000);

const TYPES = {
    '.html': 'text/html; charset=utf-8',
    '.js':   'text/javascript; charset=utf-8',
    '.mjs':  'text/javascript; charset=utf-8',
    '.css':  'text/css; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.jpg':  'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.png':  'image/png',
    '.svg':  'image/svg+xml',
    '.ico':  'image/x-icon',
    '.map':  'application/json; charset=utf-8',
};

http.createServer((req, res) => {
    let rel = decodeURIComponent((req.url || '/').split('?')[0]);
    if (rel.endsWith('/')) rel += 'index.html';
    const abs = path.normalize(path.join(ROOT, rel));
    if (!abs.startsWith(ROOT)) { res.writeHead(403); res.end('forbidden'); return; }
    fs.stat(abs, (err, st) => {
        if (err || !st.isFile()) {
            res.writeHead(404, { 'Content-Type': 'text/plain' });
            res.end('not found: ' + rel);
            return;
        }
        const type = TYPES[path.extname(abs).toLowerCase()] || 'application/octet-stream';
        res.writeHead(200, {
            'Content-Type': type,
            'Cache-Control': 'no-store',
            // Helps when WebGPU + module loading complain about isolation.
            'Cross-Origin-Opener-Policy': 'same-origin',
            'Cross-Origin-Embedder-Policy': 'require-corp',
        });
        fs.createReadStream(abs).pipe(res);
    });
}).listen(PORT, '127.0.0.1', () => {
    console.log(`water-demo serving http://localhost:${PORT}/water-demo/index.html`);
    console.log(`  root: ${ROOT}`);
});
