// Explicit real-process HTTP check. This runner never starts/stops a server.
// node .../ServerTests/Smoke.mjs http://127.0.0.1:4197 https://workshop.example
// The second argument is optional for a local-only deployment. For public
// identity checks the first address is its reachable private test listener.
import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import http from 'node:http';
import https from 'node:https';

const base = new URL(process.argv[2] || 'http://127.0.0.1:4197');
if (base.port === '4186') throw new Error('Use an isolated test port; the served 4186 instance is not a smoke target.');
const publicOrigin = process.argv[3] ? new URL(process.argv[3]).origin : null;
const authority = publicOrigin ? new URL(publicOrigin).host : base.host;
const origin = publicOrigin || base.origin;
const results = [];
function transport(url, options) {
  return new Promise((resolve, reject) => {
    const request = (url.protocol === 'https:' ? https : http).request(url, { method: options.method, headers: options.headers }, response => {
      const chunks = [];
      response.on('data', chunk => chunks.push(chunk));
      response.on('error', reject);
      response.on('end', () => resolve({ status: response.statusCode, headers: response.headers, text: Buffer.concat(chunks).toString('utf8') }));
    });
    request.on('error', reject);
    request.setTimeout(30000, () => request.destroy(new Error('Smoke request timed out.')));
    request.end(options.body);
  });
}
async function request(path, { method = 'GET', host = authority, sourceOrigin, body, contentType = 'application/json', expected = 200, extraHeaders = {} } = {}) {
  const headers = { Host: host, ...extraHeaders };
  if (sourceOrigin !== undefined) headers.Origin = sourceOrigin;
  if (method === 'POST') headers['Content-Type'] = contentType;
  const response = await transport(new URL(path, base), { method, headers, body });
  const text = response.text;
  assert.equal(response.status, expected, `${method} ${path}, Host=${host}: ${text.slice(0, 180)}`);
  assert.equal(response.headers['x-content-type-options'], 'nosniff');
  results.push({ method, path, host, origin: sourceOrigin ?? null, status: response.status });
  return { response, text };
}
const health = await request('/healthz');
assert.deepEqual(JSON.parse(health.text), { status: 'ok', version: '0.5.3' });
await request('/'); await request('/edit-model.mjs'); await request('/object-source.mjs');
const catalog = JSON.parse((await request('/api/catalog')).text); assert.equal(catalog.entries.length, 8);
const json = JSON.parse((await request('/api/catalog/full-long')).text).json;
await request('/api/analyze', { method: 'POST', sourceOrigin: origin, body: JSON.stringify({ json }) });
await request('/api/analyze/', { method: 'POST', body: JSON.stringify({ json }) }); // missing Origin CLI remains allowed
await request('/api/analyze', { method: 'POST', sourceOrigin: 'https://wrong.example', body: JSON.stringify({ json }), expected: 400 });
await request('/api/analyze', { method: 'POST', sourceOrigin: 'null', body: JSON.stringify({ json }), expected: 400 });
await request('/api/analyze', { method: 'POST', sourceOrigin: origin, body: JSON.stringify({ json }), contentType: 'text/plain', expected: 400 });
await request('/api/analyze', { method: 'POST', sourceOrigin: origin, body: '{', expected: 400 });
await request('/api/analyze', { method: 'POST', sourceOrigin: origin, body: JSON.stringify({ json: '{' }), expected: 400 });
await request('/healthz', { host: 'wrong.example', expected: 400, extraHeaders: { 'X-Forwarded-Host': authority, 'X-Forwarded-Proto': 'https' } });
if (publicOrigin) await request('/healthz', { host: base.host, expected: 400 });
for (const path of ['/ServerOptions.cs', '/README.md', '/Web/tests/model.test.mjs', '/Catalog/full-long.track.json', '/api/catalog/..%2F..%2FProgram.cs']) await request(path, { expected: 404 });
const replay = JSON.parse((await request('/api/replay', { method: 'POST', sourceOrigin: origin, body: JSON.stringify({ json, profile: 'Chosen', warningDuration: .8, axis: 'time', inputs: [] }) })).text);
assert.ok(replay.summary.finishTime > 0); assert.equal(replay.summary.status, 'Finished');
const evidence = { passed: true, base: base.origin, publicOrigin, requests: results.length, results, replaySummary: replay.summary };
await writeFile(fileURLToPath(new URL('./smoke-results.json', import.meta.url)), JSON.stringify(evidence, null, 2) + '\n');
console.log(`PASS Workshop server HTTP smoke: ${results.length} requests, version 0.5.3, identity/error/static/replay checks.`);
