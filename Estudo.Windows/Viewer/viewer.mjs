import {getDocument,GlobalWorkerOptions,TextLayer} from './pdf.mjs';
GlobalWorkerOptions.workerSrc='./pdf.worker.mjs';
const el=id=>document.getElementById(id),scroll=el('scroll'),pages=el('pages');
const send=data=>window.chrome?.webview?.postMessage(data);
let pdf,loading,source='',pageNumber=1,zoom='fit',mode='page',question={id:null,regions:[]},generation=0,openRequest=0;
let tasks=[],layers=[],draft=null,selectionStart=null,pan=null,actualScale=1,editIndex=-1;
const positions=new Map();
const key=()=>`${source}:${question.id||''}:${mode}:${mode==='page'?pageNumber:''}:${zoom}`;
function remember(){positions.set(key(),[scroll.scrollLeft,scroll.scrollTop]);}
function cancel(){generation++;for(const task of tasks)task.cancel();for(const layer of layers)layer.cancel();tasks=[];layers=[];}
function status(text){el('status').textContent=text;}
function controls(){
 el('page').value=pageNumber;el('page').max=pdf?.numPages||1;el('total').textContent=`/ ${pdf?.numPages||'—'}`;
 el('prev').disabled=!pdf||pageNumber<=1;el('next').disabled=!pdf||pageNumber>=pdf.numPages;
 el('question').disabled=!question.regions.length;el('add').disabled=!question.id;
 el('replace').disabled=!question.regions.length;el('remove').disabled=!question.regions.length;
 el('edit-actions').hidden=mode!=='edit';el('save').disabled=!draft;
 el('region').replaceChildren(...question.regions.map((r,i)=>new Option(`Trecho ${i+1} · p. ${r.page}`,i)));
 el('region').value=String(Math.max(0,editIndex));el('region').hidden=!question.regions.length;
}
async function render(restore=true,anchor=null){
 if(!pdf)return;document.body.setAttribute('aria-busy','true');cancel();const token=generation;pages.replaceChildren();draft=null;controls();
 try{
  const regions=mode==='question'?question.regions:[{page:pageNumber,x:0,y:0,width:1,height:1}];
  for(let i=0;i<regions.length;i++){
   const r=regions[i];if(r.page>pdf.numPages)throw Error('Recorte fora deste PDF. Corrija a região manualmente.');
   const page=await pdf.getPage(r.page);if(token!==generation)return;
   const base=page.getViewport({scale:1});const scale=zoom==='fit'?Math.min(4,Math.max(.15,(scroll.clientWidth-40)/(base.width*r.width))):Number(zoom);
   actualScale=scale;const viewport=page.getViewport({scale}),density=Math.min(2,devicePixelRatio||1);
   const card=document.createElement('section');card.className='page-card';
   if(mode==='question'){
    const caption=document.createElement('button');caption.className='caption';caption.textContent=`Trecho ${i+1} de ${regions.length} · página ${r.page} · abrir página inteira`;
    caption.onclick=()=>go(r.page);card.append(caption);
   }
   const paper=document.createElement('div');paper.className='paper';paper.style.width=`${viewport.width*r.width}px`;paper.style.height=`${viewport.height*r.height}px`;
   const canvas=document.createElement('canvas');canvas.width=Math.ceil(viewport.width*r.width*density);canvas.height=Math.ceil(viewport.height*r.height*density);
   canvas.style.width=paper.style.width;canvas.style.height=paper.style.height;paper.append(canvas);card.append(paper);pages.append(card);
   const task=page.render({canvasContext:canvas.getContext('2d'),viewport,transform:[density,0,0,density,-r.x*viewport.width*density,-r.y*viewport.height*density]});
   tasks.push(task);await task.promise;if(token!==generation)return;
   if(mode!=='question'){
    paper.style.setProperty('--scale-factor',scale);paper.style.setProperty('--total-scale-factor',scale);paper.style.setProperty('--user-unit',1);
    const container=document.createElement('div');container.className='textLayer';paper.append(container);
    const layer=new TextLayer({textContentSource:page.streamTextContent(),container,viewport});layers.push(layer);await layer.render();
    if(token!==generation)return;
    if(mode==='edit')attachSelection(paper);
   }
  }
  if(token!==generation)return;
  const position=restore?positions.get(key()):null;scroll.scrollTo(position?.[0]||0,position?.[1]||0);
  if(anchor)scroll.scrollTo(anchor.x*scroll.scrollWidth-anchor.clientX,anchor.y*scroll.scrollHeight-anchor.clientY);
  el('message').hidden=true;
  status(mode==='edit'?'Arraste um retângulo; depois salve. Use as páginas para adicionar outros trechos.':mode==='question'?`${regions.length} recorte(s) manual(is) · original acessível em Página inteira`:question.id?'Página inicial do índice · continuação disponível nas próximas páginas':'PDF oficial · página inteira');
  document.body.setAttribute('aria-busy','false');send({kind:'rendered',page:pageNumber,total:pdf.numPages,mode});
 }catch(error){if(token!==generation||['RenderingCancelledException','AbortException'].includes(error.name))return;document.body.setAttribute('aria-busy','false');status('Falha ao exibir a página');send({kind:'error',message:error.message});}
}
window.openDocument=async(url,page,dark,context={id:null,regions:[]})=>{
 const request=++openRequest;remember();cancel();document.body.classList.toggle('dark',dark);question=context;mode=question.regions.length?'question':'page';editIndex=-1;
 try{
  if(source!==url){source=url;const previous=loading;loading=null;pdf=null;await previous?.destroy();if(request!==openRequest)return;
   pages.replaceChildren();el('message').hidden=false;el('message').textContent='Abrindo o PDF oficial…';
   loading=getDocument({url,isEvalSupported:false,cMapUrl:'./cmaps/',cMapPacked:true,standardFontDataUrl:'./standard_fonts/',wasmUrl:'./wasm/'});
  }
  if(!pdf)pdf=await loading?.promise;if(request!==openRequest||!pdf)return;
  pageNumber=Math.max(1,Math.min(page,pdf.numPages));await render();
 }catch(error){if(request!==openRequest)return;el('message').textContent='Não foi possível abrir o PDF. Use Abrir original.';send({kind:'error',message:error.message});}
};
window.updateQuestion=async context=>{if(context.id!==question.id)return;remember();question=context;mode=question.regions.length?'question':'page';editIndex=-1;await render(false);};
window.setTheme=dark=>document.body.classList.toggle('dark',dark);
async function go(page){if(!pdf)return;remember();pageNumber=Math.max(1,Math.min(page,pdf.numPages));mode='page';await render();}
el('prev').onclick=()=>go(pageNumber-1);el('next').onclick=()=>go(pageNumber+1);el('page').onchange=()=>go(Number(el('page').value)||1);
el('full').onclick=()=>go(pageNumber);el('question').onclick=()=>{remember();mode='question';render();};
el('zoom').onchange=()=>{remember();zoom=el('zoom').value;render(false);};
function scaleBy(factor,clientX=scroll.clientWidth/2,clientY=scroll.clientHeight/2){
 if(!pdf)return;remember();const anchor={x:(scroll.scrollLeft+clientX)/scroll.scrollWidth,y:(scroll.scrollTop+clientY)/scroll.scrollHeight,clientX,clientY};
 zoom=String(Math.max(.15,Math.min(4,actualScale*factor)));
 if(!Array.from(el('zoom').options).some(o=>o.value===zoom))el('zoom').add(new Option(`${Math.round(Number(zoom)*100)}%`,zoom));
 el('zoom').value=zoom;render(false,anchor);
}
el('minus').onclick=()=>scaleBy(1/1.2);el('plus').onclick=()=>scaleBy(1.2);
scroll.addEventListener('wheel',e=>{if(!e.ctrlKey)return;e.preventDefault();const b=scroll.getBoundingClientRect();scaleBy(e.deltaY<0?1.15:1/1.15,e.clientX-b.left,e.clientY-b.top);},{passive:false});
function startEdit(index){el('region-tools').open=false;remember();editIndex=index;if(index>=0)pageNumber=question.regions[index].page;mode='edit';render(false);}
el('add').onclick=()=>startEdit(-1);el('replace').onclick=()=>startEdit(Number(el('region').value));
el('cancel').onclick=()=>{mode='page';render();};
el('remove').onclick=()=>send({kind:'region',questionId:question.id,action:'remove',index:Number(el('region').value)});
el('save').onclick=()=>{if(draft){send({kind:'region',questionId:question.id,action:editIndex<0?'add':'replace',index:editIndex,region:draft});el('save').disabled=true;}};
function attachSelection(paper){
 const overlay=document.createElement('div');overlay.className='selection-layer';const rect=document.createElement('div');rect.className='selection';overlay.append(rect);paper.append(overlay);
 const point=e=>{const b=paper.getBoundingClientRect();return{x:Math.max(0,Math.min(1,(e.clientX-b.left)/b.width)),y:Math.max(0,Math.min(1,(e.clientY-b.top)/b.height))};};
 overlay.onpointerdown=e=>{if(e.button!==0)return;selectionStart=point(e);overlay.setPointerCapture(e.pointerId);draft=null;e.preventDefault();};
 overlay.onpointermove=e=>{if(!selectionStart)return;const p=point(e);draft={page:pageNumber,x:Math.min(p.x,selectionStart.x),y:Math.min(p.y,selectionStart.y),width:Math.abs(p.x-selectionStart.x),height:Math.abs(p.y-selectionStart.y)};
  Object.assign(rect.style,{display:'block',left:`${draft.x*100}%`,top:`${draft.y*100}%`,width:`${draft.width*100}%`,height:`${draft.height*100}%`});};
 overlay.onpointerup=e=>{selectionStart=null;if(overlay.hasPointerCapture(e.pointerId))overlay.releasePointerCapture(e.pointerId);if(!draft||draft.width<.005||draft.height<.005)draft=null;el('save').disabled=!draft;};
 overlay.onpointercancel=()=>{selectionStart=null;draft=null;el('save').disabled=true;};
}
scroll.addEventListener('pointerdown',e=>{if(e.button!==1)return;pan={x:e.clientX,y:e.clientY};scroll.setPointerCapture(e.pointerId);e.preventDefault();});
scroll.addEventListener('pointermove',e=>{if(!pan)return;scroll.scrollBy(pan.x-e.clientX,pan.y-e.clientY);pan={x:e.clientX,y:e.clientY};});
scroll.addEventListener('pointerup',e=>{if(!pan)return;pan=null;scroll.releasePointerCapture(e.pointerId);});
let resize;new ResizeObserver(()=>{clearTimeout(resize);resize=setTimeout(()=>{if(zoom==='fit'){remember();render();}},160);}).observe(scroll);
window.addEventListener('keydown',e=>{if(e.ctrlKey&&e.altKey){const action=e.key==='ArrowRight'?'next':e.key==='ArrowLeft'?'previous':/^[a-e]$/i.test(e.key)?e.key.toUpperCase():null;if(action){e.preventDefault();send({kind:'shortcut',action});}}});
send({kind:'ready'});
