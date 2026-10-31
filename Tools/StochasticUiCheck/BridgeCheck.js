'use strict';
const fs=require('fs'),vm=require('vm'),path=require('path'),assert=require('assert');
const root=process.argv[2];let tick=0,step=1,checks=0;
const canvas={dataset:{}},queue=[],context={LibraryManager:{library:{}},Module:{canvas},UTF8ToString:x=>x,queueMicrotask:f=>queue.push(f),performance:{now:()=>{tick+=step;return tick;},timeOrigin:0},Map};
context.mergeInto=(target,source)=>Object.assign(target,source);vm.createContext(context);
vm.runInContext(fs.readFileSync(path.join(root,'Assets/Plugins/WebGL/FlipDiagnostics.jslib'),'utf8'),context);
const html=fs.readFileSync(path.join(root,'Assets/WebGLTemplates/Flip/index.html'),'utf8');
const begin=html.indexOf('  function timedDiagnosticObserver('),end=html.indexOf('  // The same snapshot',begin);
assert(begin>=0&&end>begin);checks++;
context.canvas=canvas;vm.runInContext(html.slice(begin,end),context);
for(const [name,index] of [['positionFullscreenButton',0],['positionCopyButton',1],['describeControls',2]]){assert(html.includes(`new MutationObserver(timedDiagnosticObserver(${name},${index}))`));checks++;}
const bridge=context.LibraryManager.library;
bridge.FlipBotPerformance('');
const wrapped=[0,1,2].map(i=>context.timedDiagnosticObserver(()=>{tick+=2;},i));
bridge.FlipReport('{"frame":1}',1,0,123);wrapped.forEach(f=>f());
assert.equal(canvas.flipBotDiagnosticAudit.samples.length,3);checks++;
const raw={schema:1,attemptSeed:123,capacity:10000,totalFrames:4,overflowFrames:0,activeCaptureFaultFrames:0,terminalFrame:4,frames:[
{frame:1,cohort:'Active',advanceMilliseconds:1,diagnosticsMilliseconds:2,planCalls:1},
{frame:2,cohort:'Active',advanceMilliseconds:0,diagnosticsMilliseconds:0,planCalls:0},
{frame:3,cohort:'Paused',advanceMilliseconds:200,diagnosticsMilliseconds:200,planCalls:0},
{frame:4,cohort:'Active',advanceMilliseconds:75,diagnosticsMilliseconds:0,planCalls:1}]};
bridge.FlipBotPerformance(JSON.stringify(raw));assert(!canvas.dataset.flipBotPerformance);checks++;
queue.shift()();let exported=JSON.parse(canvas.dataset.flipBotPerformance);
assert.equal(exported.frames.length,4);assert.equal(exported.activeAdvanceDiagnosticsAndCallbacks.count,3);checks+=2;
const callbacks=exported.browserCallbacks.samples.reduce((s,c)=>s+c.milliseconds,0);
assert.equal(exported.activeAdvanceDiagnosticsAndCallbacks.sumMilliseconds,1+2+75+callbacks);checks++;
assert.equal(exported.activeAdvanceDiagnosticsAndCallbacks.over50,1);assert(exported.measurementComplete);checks+=2;
bridge.FlipBotPerformance('');
const faultRaw=Object.assign({},raw,{activeCaptureFaultFrames:1});
bridge.FlipBotPerformance(JSON.stringify(faultRaw));queue.shift()();
assert.equal(JSON.parse(canvas.dataset.flipBotPerformance).measurementComplete,false);checks++;
const persisted=canvas.dataset.flipBotPerformance;
for(let i=0;i<20;i++){bridge.FlipReport('{}',100+i,-1,123);wrapped.forEach(f=>f());}
assert.equal(canvas.dataset.flipBotPerformance,persisted);assert.equal(canvas.flipBotDiagnosticAudit.samples.length,0);checks+=2;
bridge.FlipBotPerformance('');assert(!canvas.dataset.flipBotPerformance);assert(!canvas.dataset.flipBotPerformanceBrowserExportMilliseconds);checks+=2;
const audit=canvas.flipBotDiagnosticAudit,ctx={frame:1,ordinal:1,cohort:0,seed:123};
for(let i=0;i<10001;i++)audit.record(0,0,ctx);
assert.equal(audit.samples.length,10000);assert.equal(audit.overflowCalls,1);checks+=2;
step=100;bridge.FlipBotPerformance(JSON.stringify(raw));queue.shift()();exported=JSON.parse(canvas.dataset.flipBotPerformance);
assert.equal(exported.measurementComplete,false);assert.equal(canvas.dataset.flipBotPerformanceBrowserExportOver50,'true');checks+=2;
console.log(JSON.stringify({status:'PASS',checks,scope:'Actual bridge and actual HTML timing wrapper executed in a deterministic Node VM. Tests association, all zero-plan frames, async terminal join, persistent export, reset, overflow and separately retained >50ms export costs. Synthetic clock/DOM, not browser timing evidence.'}));