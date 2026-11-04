// Local integration through the real Caddy TLS + Basic Auth deployment.
// node ProxySmoke.mjs https://localhost:4191 public-ca.crt test-password-file result.json
import assert from 'node:assert/strict';
import fs from 'node:fs';
import https from 'node:https';

const [origin, caFile, passwordFile, output] = process.argv.slice(2);
if (!origin || !caFile || !passwordFile || !output) throw new Error('Expected origin, CA, ephemeral password file, output');
const base = new URL(origin);
const ca = fs.readFileSync(caFile);
const authorization = 'Basic ' + Buffer.from('workshop-test:' + fs.readFileSync(passwordFile, 'utf8').trim()).toString('base64');
const evidence = [];
function request(path, {body, auth = authorization, headers = {}} = {}) {
  const payload = body === undefined ? undefined : JSON.stringify(body);
  return new Promise((resolve, reject) => {
    const req = https.request(new URL(path, base), {
      method: payload === undefined ? 'GET' : 'POST', ca, family: 4,
      headers: {...(auth ? {Authorization: auth} : {}), ...(payload ? {'Content-Type': 'application/json', Origin: base.origin} : {}), ...headers}
    }, res => {
      let text = '';
      const tlsVerified = res.socket.authorized;
      res.setEncoding('utf8');
      res.on('data', chunk => { text += chunk; });
      res.on('end', () => resolve({status: res.statusCode, headers: res.headers, text, tlsVerified}));
    });
    req.on('error', reject);
    req.setTimeout(30000, () => req.destroy(new Error('Proxy smoke timed out')));
    req.end(payload);
  });
}
async function expect(name, path, status, options) {
  const result = await request(path, options);
  assert.equal(result.status, status, name + ': ' + result.text.slice(0, 150));
  evidence.push({name, status: result.status});
  return result;
}

await expect('UI requires password', '/', 401, {auth: null});
await expect('API requires password', '/api/catalog', 401, {auth: null});
await expect('Health requires password', '/healthz', 401, {auth: null});
await expect('Wrong password rejected', '/healthz', 401, {auth: 'Basic ' + Buffer.from('workshop-test:wrong').toString('base64')});
const health = await expect('Authenticated health through verified TLS', '/healthz', 200);
assert.equal(JSON.parse(health.text).version, '0.4.0');
assert.equal(health.tlsVerified, true);
const page = await expect('Authenticated UI', '/', 200);
assert.match(page.text, /0\.4 · Unity Core/);
await expect('Editor module', '/edit-model.mjs', 200);
await expect('No project file serving', '/ServerOptions.cs', 404);
const catalog = JSON.parse((await expect('Catalog', '/api/catalog', 200)).text);
assert.equal(catalog.entries.length, 8);
const {json} = JSON.parse((await expect('Track source', '/api/catalog/mixed-tricks', 200)).text);
const analysis = JSON.parse((await expect('Same-origin analyze', '/api/analyze', 200, {body: {json}})).text);
assert.equal(analysis.gameValid, true);
await expect('Foreign Origin rejected', '/api/analyze', 400, {body: {json}, headers: {Origin: 'https://foreign.invalid'}});
await expect('Wrong content type rejected', '/api/analyze', 400, {body: {json}, headers: {'Content-Type': 'text/plain'}});
await expect('No-Origin CLI supported', '/api/analyze', 200, {body: {json}, headers: {Origin: ''}});
await expect('Forwarded identity is ignored', '/api/analyze', 200, {body: {json}, headers: {'X-Forwarded-Host': 'foreign.invalid', 'X-Forwarded-Proto': 'http'}});
await expect('Invalid JSON track returns 400', '/api/analyze/', 400, {body: {json: '{'}});
const references = JSON.parse(json).botReferences;
const summaries = [];
for (const profile of ['Chosen', 'FastFlip']) {
  const result = JSON.parse((await expect('Replay ' + profile, '/api/replay', 200, {body: {json, profile, warningDuration: .8, axis: 'distance', inputs: references.find(r => r.profileId === profile).inputs}})).text);
  assert.equal(result.summary.status, 'Finished');
  assert.equal(result.summary.collisions, 0);
  assert.ok(Math.abs(result.summary.finishTime - 33.05) < 1e-7);
  summaries.push({profile, ...result.summary});
}
fs.writeFileSync(output, JSON.stringify({passed: true, origin: base.origin, tls: 'Verified with explicit test CA; no insecure TLS option', checks: evidence.length, evidence, summaries}, null, 2) + '\n');
console.log(JSON.stringify({passed: true, checks: evidence.length, times: summaries.map(s => ({profile: s.profile, finishTime: s.finishTime}))}));
