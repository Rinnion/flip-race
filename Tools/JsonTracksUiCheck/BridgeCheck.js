const fs=require('fs'),path=require('path'),vm=require('vm'),assert=require('assert');
const html=fs.readFileSync(path.join(process.argv[2],'Assets/WebGLTemplates/Flip/index.html'),'utf8');
const script=[...html.matchAll(/<script(?:\s[^>]*)?>([\s\S]*?)<\/script>/g)].map(x=>x[1]).find(x=>x.includes('const canvas ='));
assert(script,'actual production template script');
let checks=0,chooser=0;const calls=[],nodes=new Map(),observers=[];
function node(id){if(nodes.has(id))return nodes.get(id);const n={id,hidden:false,disabled:false,textContent:'',value:'',files:[],style:{},dataset:{},clientWidth:568,clientHeight:320,width:568,height:320,
  listeners:{},children:[],addEventListener(name,fn){this.listeners[name]=fn;},getBoundingClientRect(){return{left:0,top:0};},focus(){},appendChild(c){this.children.push(c);},remove(){},select(){},setSelectionRange(){},getAttribute(){return '';},setAttribute(){},click(){if(id==='track-json-file')chooser++;}};nodes.set(id,n);return n;}
const document={getElementById:node,documentElement:{},fullscreenEnabled:false,hidden:false,addEventListener(){},createElement:name=>node('created-'+name),body:{appendChild(){}},execCommand:()=>true};
const context={document,console,navigator:{},innerWidth:568,innerHeight:320,performance:{now:()=>1},MutationObserver:class{constructor(fn){this.fn=fn;observers.push(this);}observe(){}},setTimeout,clearTimeout,
  createUnityInstance:()=>Promise.resolve({SendMessage:(...args)=>calls.push(args)})};context.window=context;context.addEventListener=()=>{};
vm.createContext(context);vm.runInContext(script.replace(/\{\{\{[\s\S]*?\}\}\}/g,'"test"'),context);
function check(ok,why){checks++;assert(ok,why);}
(async()=>{
  await Promise.resolve();check(!context.flipDiagnostics.loaded,'Unity boot alone must not unblock play');
  context.flipTrackFiles.catalog('error','index.json отсутствует <literal>');check(node('catalog-error').textContent==='index.json отсутствует <literal>'&&!node('retry-tracks').hidden,'literal visible catalogue error');
  node('retry-tracks').listeners.click();check(calls.pop()[1]==='RetryTrackCatalog','real retry routes to receiver');
  context.flipTrackFiles.catalog('ready','готово');check(context.flipDiagnostics.loaded&&node('loading').style.display==='none','catalogue plus Unity ready unblocks overlay');
  node('unity-canvas').dataset.flipState=JSON.stringify({trackImportVisible:true,trackImportX:380,trackImportY:214,trackImportWidth:165,trackImportHeight:44});
  vm.runInContext('positionCopyButton()',context);check(!node('import-track').hidden&&node('import-track').style.width==='165px','production rectangle positions real import button');
  node('import-track').listeners.click({stopPropagation(){}});check(chooser===1,'real button invokes file input synchronously');
  const json='{"track":{"name":"<literal>"}}';node('track-json-file').files=[{size:json.length,text:async()=>json}];await node('track-json-file').listeners.change();
  check(calls.at(-1)[1]==='ImportTrackFile'&&calls.at(-1)[2]===json,'chosen file text crosses bridge unmodified, no envelope');
  context.flipTrackFiles.result(true,'Добавлено <literal>');check(node('track-import-status').textContent==='Добавлено <literal>'&&!node('import-track').disabled,'result is literal and chooser enabled again');
  let read=false;node('track-json-file').files=[{size:2097153,text:async()=>{read=true;return '';}}];await node('track-json-file').listeners.change();
  check(!read&&calls.at(-1)[1]==='ImportTrackError','oversize rejects before reading');
  const count=calls.length;node('track-json-file').files=[];await node('track-json-file').listeners.change();node('track-json-file').listeners.cancel();check(calls.length===count,'empty/cancel never imports or replaces selection');
  node('track-json-file').files=[{size:1,text:async()=>{throw new Error('Чтение не удалось');}}];await node('track-json-file').listeners.change();
  check(calls.at(-1)[1]==='ImportTrackError'&&calls.at(-1)[2]==='Чтение не удалось','read failure routes to visible managed error');
  check(observers.length===3,'no additional per-frame diagnostic observer');
  console.log(JSON.stringify({status:'PASS',checks,scope:'Production HTML template in a bounded DOM/file-reader environment. Does not replace actual native browser file chooser verification.'}));
})().catch(error=>{console.error(error);process.exitCode=1;});
