import fs from 'node:fs/promises';
import crypto from 'node:crypto';
const manifest=JSON.parse(await fs.readFile('Releases/flip-race-v5.2.0-trial-webgl.zip.manifest.json','utf8'));
const rows=[];
for(const base of process.argv.slice(2)){
 const files=[];
 for(const file of manifest.files){
  const response=await fetch(new URL(file.path,base),{signal:AbortSignal.timeout(15000)});
  const bytes=Buffer.from(await response.arrayBuffer());const sha=crypto.createHash('sha256').update(bytes).digest('hex');
  if(response.status!==200||bytes.length!==file.bytes||sha!==file.sha256)throw Error('HTTP mismatch '+base+file.path);
  if(response.headers.get('cache-control')!=='no-store')throw Error('Unexpected cache policy');
  if(file.path==='index.html'&&(!bytes.toString().includes('5.2.0-trial')||!bytes.toString().includes('state.bot ?')))throw Error('Version or bot accessibility branch missing');
  files.push({path:file.path,status:response.status,bytes:bytes.length,sha256:sha,contentType:response.headers.get('content-type'),cacheControl:response.headers.get('cache-control'),allMatch:true});
 }
 rows.push({base,files});
}
const report={status:'PASS',checkedAt:new Date().toISOString(),archiveSha256:manifest.archive.sha256,version:'5.2.0-trial',checks:rows,limitation:'HTTP bytes only; native browser and dynamic aria checked separately.'};
await fs.writeFile('Verification/bots-http.json',JSON.stringify(report,null,2)+'\n');console.log('PASS HTTP '+rows.length+' origins × '+manifest.fileCount+' files');