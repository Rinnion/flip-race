#!/usr/bin/env node
import { createReadStream } from 'node:fs';
import { realpath, stat } from 'node:fs/promises';
import { createServer } from 'node:http';
import { networkInterfaces } from 'node:os';
import path from 'node:path';
import { pipeline } from 'node:stream/promises';
import { fileURLToPath } from 'node:url';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const root = await realpath(path.resolve(process.argv[2] || path.join(projectRoot, 'Builds', 'WebGL')));
const host = process.env.HOST || '0.0.0.0';
const port = Number(process.env.PORT || 4180);
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('PORT must be 1..65535');
if (!(await stat(path.join(root, 'index.html'))).isFile()) throw new Error(`Missing index.html: ${root}`);

const types = new Map(Object.entries({
  '.html': 'text/html; charset=utf-8', '.js': 'application/javascript; charset=utf-8',
  '.mjs': 'application/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.wasm': 'application/wasm', '.data': 'application/octet-stream',
  '.json': 'application/json; charset=utf-8', '.txt': 'text/plain; charset=utf-8',
  '.svg': 'image/svg+xml', '.png': 'image/png', '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.gif': 'image/gif',
  '.ico': 'image/x-icon', '.woff': 'font/woff', '.woff2': 'font/woff2',
  '.ttf': 'font/ttf', '.otf': 'font/otf', '.wav': 'audio/wav',
  '.mp3': 'audio/mpeg', '.ogg': 'audio/ogg', '.unityweb': 'application/octet-stream',
}));

function isWithinRoot(target) {
  const relative = path.relative(root, target);
  return relative === '' || (!relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative));
}

function error(response, code, message) {
  response.writeHead(code, { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store' });
  response.end(message);
}

const server = createServer(async (request, response) => {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    response.setHeader('Allow', 'GET, HEAD');
    error(response, 405, 'Only GET and HEAD are supported.');
    return;
  }
  try {
    // Decode once, before resolving. Do not let Windows separators, drive paths,
    // alternate data streams or dotfiles escape the public build directory.
    const pathname = decodeURIComponent((request.url || '/').split('?')[0]);
    const segments = pathname.split('/');
    if (!pathname.startsWith('/') || /[\\:\x00-\x1f]/.test(pathname)
        || segments.some(segment => segment.startsWith('.'))) {
      error(response, 403, 'Forbidden path.');
      return;
    }
    const candidate = path.resolve(root, `.${pathname}`);
    if (!isWithinRoot(candidate)) { error(response, 403, 'Forbidden path.'); return; }
    let target = await realpath(candidate);
    if (!isWithinRoot(target)) { error(response, 403, 'Forbidden path.'); return; }
    let info = await stat(target);
    if (info.isDirectory()) {
      target = await realpath(path.join(target, 'index.html'));
      if (!isWithinRoot(target)) { error(response, 403, 'Forbidden path.'); return; }
      info = await stat(target);
    }
    if (!info.isFile()) { error(response, 404, 'Not found.'); return; }

    let logicalName = target;
    let encoding;
    if (logicalName.endsWith('.gz')) { logicalName = logicalName.slice(0, -3); encoding = 'gzip'; }
    if (logicalName.endsWith('.br')) { logicalName = logicalName.slice(0, -3); encoding = 'br'; }
    const headers = {
      'Content-Type': types.get(path.extname(logicalName).toLowerCase()) || 'application/octet-stream',
      'Content-Length': info.size,
      'Cache-Control': 'no-store',
      'X-Content-Type-Options': 'nosniff',
    };
    if (encoding) headers['Content-Encoding'] = encoding;
    response.writeHead(200, headers);
    if (request.method === 'HEAD') response.end();
    else await pipeline(createReadStream(target), response);
  } catch (cause) {
    if (response.headersSent) { response.destroy(); return; }
    if (cause instanceof URIError) error(response, 400, 'Malformed URL.');
    else if (['ENOENT', 'ENOTDIR'].includes(cause.code)) error(response, 404, 'Not found.');
    else if (['EACCES', 'EPERM'].includes(cause.code)) error(response, 403, 'Forbidden path.');
    else { console.error(cause); error(response, 500, 'Server error.'); }
  }
});

server.on('error', cause => { console.error(cause.message); process.exitCode = 1; });
server.listen(port, host, () => {
  console.log(`Unity Web build: ${root}`);
  console.log(`Local: http://localhost:${port}/`);
  if (host === '0.0.0.0' || host === '::') {
    for (const [name, addresses] of Object.entries(networkInterfaces())) {
      for (const address of addresses || []) {
        if (address.family === 'IPv4' && !address.internal) console.log(`Phone on the same Wi-Fi (${name}): http://${address.address}:${port}/`);
      }
    }
  } else console.log(`Bound address: http://${host}:${port}/`);
  console.log('Keep this process running. Ctrl+C stops the server.');
  console.log('Brotli over LAN HTTP may require HTTPS; the release uses gzip or uncompressed files.');
});
