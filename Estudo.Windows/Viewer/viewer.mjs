import {getDocument,GlobalWorkerOptions,TextLayer} from './pdf.mjs';
GlobalWorkerOptions.workerSrc='./pdf.worker.mjs';
let pdf,loading,pageNumber=1,zoom='fit',generation=0,openRequest=0,rendering,layer,source='',positions=new Map();
const el=id=>document.getElementById(id), scroll=el('scroll'),paper=el('paper'),canvas=document.querySelector('canvas');
const send=data=>window.chrome?.webview?.postMessage(data);
const positionKey=()=>`${source}:${pageNumber}:${zoom}`;
function remember(){positions.set(positionKey(),[scroll.scrollLeft,scroll.scrollTop]);}
async function render(restore=true){
 if(!pdf)return; const token=++generation; rendering?.cancel();layer?.cancel();
 try{
  const page=await pdf.getPage(pageNumber);if(token!==generation)return;
  const base=page.getViewport({scale:1}); const scale=zoom==='fit'?Math.max(.25,(scroll.clientWidth-40)/base.width):Number(zoom);
  const viewport=page.getViewport({scale}); const density=Math.min(2,devicePixelRatio||1);
  paper.style.width=`${viewport.width}px`;paper.style.height=`${viewport.height}px`;
  paper.style.setProperty('--scale-factor',scale);paper.style.setProperty('--total-scale-factor',scale);paper.style.setProperty('--user-unit',1);
  canvas.width=Math.floor(viewport.width*density);canvas.height=Math.floor(viewport.height*density);canvas.style.width=`${viewport.width}px`;canvas.style.height=`${viewport.height}px`;
  rendering=page.render({canvasContext:canvas.getContext('2d'),viewport,transform:[density,0,0,density,0,0]});await rendering.promise;
  if(token!==generation)return;
  const container=document.querySelector('.textLayer');container.replaceChildren();
  layer=new TextLayer({textContentSource:page.streamTextContent(),container,viewport});await layer.render();
  if(token!==generation)return;
  el('page').value=pageNumber;el('page').max=pdf.numPages;el('total').textContent=`/ ${pdf.numPages}`;el('prev').disabled=pageNumber===1;el('next').disabled=pageNumber===pdf.numPages;
  const p=restore?positions.get(positionKey()):null;scroll.scrollTo(p?.[0]||0,p?.[1]||0);
  el('message').hidden=true;paper.hidden=false;el('status').textContent='PDF oficial · página inteira';
  send({kind:'rendered',page:pageNumber,total:pdf.numPages});
 }catch(error){if(error.name==='RenderingCancelledException'||error.name==='AbortException')return;el('status').textContent='Falha ao exibir a página';send({kind:'error',message:error.message});}
}
window.openDocument=async(url,page,dark)=>{
 const request=++openRequest;
 document.body.classList.toggle('dark',dark);remember();
 try{
  if(source!==url){source=url;generation++;rendering?.cancel();layer?.cancel();const previous=loading;loading=null;pdf=null;await previous?.destroy();if(request!==openRequest)return;paper.hidden=true;el('message').hidden=false;el('message').textContent='Abrindo o PDF oficial…';loading=getDocument({url,isEvalSupported:false,cMapUrl:'./cmaps/',cMapPacked:true,standardFontDataUrl:'./standard_fonts/',wasmUrl:'./wasm/'});}
  if(!pdf)pdf=await loading?.promise;
  if(request!==openRequest||!pdf)return;
  pageNumber=Math.max(1,Math.min(page,pdf.numPages));await render();
 }catch(error){el('message').textContent='Não foi possível abrir o PDF. Use Abrir original.';send({kind:'error',message:error.message});}
};
window.setTheme=dark=>document.body.classList.toggle('dark',dark);
async function go(page){if(!pdf)return;remember();pageNumber=Math.max(1,Math.min(page,pdf.numPages));await render();}
el('prev').onclick=()=>go(pageNumber-1);el('next').onclick=()=>go(pageNumber+1);el('page').onchange=()=>go(Number(el('page').value)||1);
el('zoom').onchange=()=>{remember();zoom=el('zoom').value;render(false);};
function scaleBy(factor){remember();const width=paper.clientWidth;const current=zoom==='fit'?width/612:Number(zoom);zoom=String(Math.max(.25,Math.min(4,current*factor)));let option=Array.from(el('zoom').options).find(o=>o.value===zoom);if(!option){option=new Option(`${Math.round(Number(zoom)*100)}%`,zoom);el('zoom').add(option);}el('zoom').value=zoom;render(false);}
el('minus').onclick=()=>scaleBy(1/1.2);el('plus').onclick=()=>scaleBy(1.2);
let resize;new ResizeObserver(()=>{clearTimeout(resize);resize=setTimeout(()=>{remember();if(zoom==='fit')render();},160);}).observe(scroll);
window.addEventListener('keydown',event=>{if(event.ctrlKey&&event.altKey){let action;if(event.key==='ArrowRight')action='next';if(event.key==='ArrowLeft')action='previous';if(/^[a-e]$/i.test(event.key))action=event.key.toUpperCase();if(action){event.preventDefault();send({kind:'shortcut',action});}}});
send({kind:'ready'});
