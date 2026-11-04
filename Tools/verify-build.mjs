#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { lstat, mkdir, readFile, readdir, realpath, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { brotliDecompressSync, gunzipSync } from 'node:zlib';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const fail = message => { throw new Error(message); };
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');

export async function verifyBuild(buildDirectory, { requireTrackCatalog = false } = {}) {
  const root = await realpath(buildDirectory);
  const files = [];
  async function walk(directory, prefix = '') {
    for (const item of await readdir(directory, { withFileTypes: true })) {
      const name = `${prefix}${item.name}`;
      const full = path.join(directory, item.name);
      const info = await lstat(full);
      if (info.isSymbolicLink()) fail(`Symlink/reparse point is not allowed in build: ${name}`);
      if (item.name.startsWith('.')) fail(`Unexpected hidden file in public build: ${name}`);
      if (info.isDirectory()) await walk(full, `${name}/`);
      else if (info.isFile()) {
        if (name.length > 240) fail(`itch.io path limit exceeded: ${name}`);
        if (info.size > 200_000_000) fail(`itch.io single-file limit exceeded: ${name}`);
        if (name.includes('\\') || name.includes(':')) fail(`Invalid portable path: ${name}`);
        files.push({ path: name, bytes: info.size, sha256: sha256(await readFile(full)) });
      }
    }
  }
  await walk(root);
  files.sort((a, b) => a.path.localeCompare(b.path, 'en'));
  const paths = new Set(files.map(file => file.path));
  const size = files.reduce((total, file) => total + file.bytes, 0);
  if (!paths.has('index.html')) fail('index.html must be at the build/ZIP root (exact case).');
  if (files.length > 1000 || size > 500_000_000) fail('itch.io extracted file-count or total-size limit exceeded.');
  const html = await readFile(path.join(root, 'index.html'), 'utf8');
  if (/\{\{\{|<%/.test(html)) fail('Unexpanded Unity template placeholders in index.html.');
  const references = new Set();

  function checkReference(value, from = 'index.html') {
    value = value.replace(/&amp;/g, '&');
    if (!value || value.startsWith('#') || value.startsWith('data:') || value.startsWith('blob:')) return;
    if (/^(?:[a-z][a-z\d+.-]*:|\/|\\)/i.test(value)) fail(`Runtime reference must be relative: ${value} (${from})`);
    const clean = decodeURIComponent(value.split(/[?#]/)[0]);
    if (clean.includes('\\') || clean.includes(':')) fail(`Invalid reference: ${value}`);
    const resolved = path.posix.normalize(path.posix.join(path.posix.dirname(from), clean));
    if (resolved.startsWith('../') || !paths.has(resolved)) fail(`Missing resource or wrong filename case: ${value} (${from})`);
    references.add(resolved);
  }

  for (const match of html.matchAll(/\b(?:src|href)\s*=\s*(["'])(.*?)\1/gi)) checkReference(match[2]);
  // Read the small, declarative URL configuration in the Unity HTML template;
  // never evaluate JavaScript from the build.
  const constants = new Map();
  for (const match of html.matchAll(/\b(?:var|let|const)\s+(\w+)\s*=\s*(["'])([^"'\r\n]*)\2\s*;/g)) constants.set(match[1], match[3]);
  function resolveExpression(expression) {
    const terms = expression.trim().split(/\s*\+\s*/);
    return terms.map(term => {
      const literal = term.match(/^(["'])(.*)\1$/);
      if (literal) return literal[2];
      if (constants.has(term)) return constants.get(term);
      fail(`Cannot verify runtime URL expression: ${expression}`);
    }).join('');
  }
  const runtime = {};
  for (const match of html.matchAll(/\b(loaderUrl|dataUrl|frameworkUrl|codeUrl|memoryUrl|symbolsUrl)\s*[:=]\s*([^;,\r\n}]+)/g)) {
    const value = resolveExpression(match[2]);
    constants.set(match[1], value);
    checkReference(value);
    runtime[match[1]] = value;
  }
  if (!runtime.loaderUrl) {
    const loader = [...references].find(value => /\.loader\.js(?:\.(?:gz|br))?$/.test(value));
    if (loader) runtime.loaderUrl = loader;
  }
  for (const required of ['loaderUrl', 'dataUrl', 'frameworkUrl', 'codeUrl']) {
    if (!runtime[required]) fail(`Missing Unity runtime URL in index.html: ${required}`);
  }
  const compression = value => value.endsWith('.gz') ? 'gzip' : value.endsWith('.br') ? 'brotli' : value.endsWith('.unityweb') ? 'unity-fallback' : 'disabled';
  const formats = new Set(['dataUrl', 'frameworkUrl', 'codeUrl'].map(key => compression(runtime[key])));
  if (formats.size !== 1) fail('Unity data/framework/wasm compression settings do not agree.');
  for (const file of files.filter(file => file.path.endsWith('.css'))) {
    const css = await readFile(path.join(root, file.path), 'utf8');
    for (const match of css.matchAll(/url\(\s*['"]?([^)'"\s]+)['"]?\s*\)/gi)) checkReference(match[1], file.path);
  }
  const wasmPath = runtime.codeUrl.split(/[?#]/)[0];
  let wasm = await readFile(path.join(root, wasmPath));
  const format = [...formats][0];
  let fallbackCodec;
  if (format === 'gzip') wasm = gunzipSync(wasm);
  if (format === 'brotli') wasm = brotliDecompressSync(wasm);
  if (format === 'unity-fallback') {
    fallbackCodec = wasm[0] === 0x1f && wasm[1] === 0x8b ? 'gzip' : 'brotli';
    wasm = fallbackCodec === 'gzip' ? gunzipSync(wasm) : brotliDecompressSync(wasm);
    for (const key of ['dataUrl', 'frameworkUrl']) {
      const bytes = await readFile(path.join(root, runtime[key]));
      if (fallbackCodec === 'gzip' && (bytes[0] !== 0x1f || bytes[1] !== 0x8b)) fail(`Expected gzip fallback content: ${runtime[key]}`);
    }
  }
  if (!wasm.subarray(0, 8).equals(Buffer.from([0, 97, 115, 109, 1, 0, 0, 0]))) fail('Invalid WebAssembly header.');
  let trackCatalog;
  const cataloguePath = 'StreamingAssets/Tracks/index.json';
  if (requireTrackCatalog && !paths.has(cataloguePath)) fail(`Missing track catalogue: ${cataloguePath}`);
  if (paths.has(cataloguePath)) {
    const catalogue = JSON.parse(await readFile(path.join(root, cataloguePath), 'utf8'));
    if (!Array.isArray(catalogue.files) || catalogue.files.length === 0
        || new Set(catalogue.files).size !== catalogue.files.length) fail('Track catalogue must list distinct files.');
    const tracks = [], ids = new Set();
    for (const name of catalogue.files) {
      if (typeof name !== 'string' || !/^[a-zA-Z0-9._-]+\.json$/.test(name)) fail(`Invalid track filename: ${name}`);
      checkReference(name, cataloguePath);
      const documentPath = path.posix.join(path.posix.dirname(cataloguePath), name);
      const document = JSON.parse(await readFile(path.join(root, documentPath), 'utf8'));
      if (!document.track || typeof document.track.id !== 'string' || !document.track.id
          || ids.has(document.track.id)) fail(`Missing or duplicate track ID: ${name}`);
      ids.add(document.track.id);
      tracks.push({ path: documentPath, id: document.track.id, schemaVersion: document.schemaVersion });
    }
    trackCatalog = { path: cataloguePath, count: tracks.length, tracks };
  }
  return {
    checkedAt: new Date().toISOString(), buildDirectory: root, fileCount: files.length,
    totalBytes: size, totalMiB: Number((size / 1048576).toFixed(3)), compression: format,
    ...(fallbackCodec ? { fallbackCodec } : {}),
    checkedReferences: [...references].sort(), runtime, files,
    ...(trackCatalog ? { trackCatalog } : {}),
    note: 'Static artifact verification only. Browser launch, gameplay and physical multi-touch require separate checks.',
  };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const args = process.argv.slice(2);
    const jsonIndex = args.indexOf('--json');
    const reportPath = jsonIndex >= 0 ? args[jsonIndex + 1] : null;
    if (jsonIndex >= 0 && !reportPath) fail('--json requires a report path.');
    const directory = args[0] && !args[0].startsWith('--') ? path.resolve(args[0]) : path.join(projectRoot, 'Builds', 'WebGL');
    const report = await verifyBuild(directory, { requireTrackCatalog: args.includes('--require-track-catalog') });
    if (reportPath) {
      await mkdir(path.dirname(path.resolve(reportPath)), { recursive: true });
      await writeFile(reportPath, `${JSON.stringify(report, null, 2)}\n`);
    }
    console.log(`Verified: ${report.fileCount} files, ${report.totalBytes} bytes (${report.totalMiB} MiB), compression=${report.compression}`);
    console.log(`Relative resources checked: ${report.checkedReferences.length}; WebAssembly header checked.`);
    if (reportPath) console.log(`Manifest: ${path.resolve(reportPath)}`);
  } catch (cause) { console.error(`Build verification failed: ${cause.message}`); process.exitCode = 1; }
}
