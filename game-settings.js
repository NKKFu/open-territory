const defaults={quality:'auto',fov:58,volume:100,sensitivity:100,invertY:false};
const storageKey='open-territory.settings.v1';
function numberInRange(value,lo,hi,fallback) {
  return Number.isFinite(value)?Math.min(hi,Math.max(lo,Math.round(value))):fallback;
}
function load() {
  let saved;
  try { saved=JSON.parse(localStorage.getItem(storageKey)); } catch {}
  return {
    quality:['auto','low','high'].includes(saved?.quality)?saved.quality:defaults.quality,
    fov:numberInRange(saved?.fov,48,80,defaults.fov),
    volume:numberInRange(saved?.volume,0,100,defaults.volume),
    sensitivity:numberInRange(saved?.sensitivity,40,180,defaults.sensitivity),
    invertY:saved?.invertY===true
  };
}
export function createSettings({apply,onPause,canvas}) {
  const dialog=document.querySelector('#settings'),values=load();
  const tabs=[...dialog.querySelectorAll('[role=tab]')];
  function sync(){
    for(const input of dialog.querySelectorAll('input')){
      const value=values[input.name];
      if(input.type==='radio')input.checked=value===input.value;
      else if(input.type==='checkbox')input.checked=value;
      else{
        input.value=value;
        const unit=input.name==='fov'?'°':'%';
        dialog.querySelector(`output[for="${input.id}"]`).value=value+unit;
        input.setAttribute('aria-valuetext',value+unit);
      }
    }
    apply(values);
  }
  function save(){
    sync();
    try {localStorage.setItem(storageKey,JSON.stringify(values));} catch {}
  }
  function selectTab(tab){
    for(const button of tabs){
      const active=button===tab;button.setAttribute('aria-selected',String(active));button.tabIndex=active?0:-1;
      document.getElementById(button.getAttribute('aria-controls')).hidden=!active;
    }
  }
  for(const tab of tabs){
    tab.addEventListener('click',()=>selectTab(tab));
    tab.addEventListener('keydown',e=>{
      const index=tabs.indexOf(tab);
      const next={ArrowRight:(index+1)%tabs.length,ArrowLeft:(index+tabs.length-1)%tabs.length,Home:0,End:tabs.length-1}[e.key];
      if(next===undefined)return;
      e.preventDefault();selectTab(tabs[next]);tabs[next].focus();
    });
  }
  dialog.addEventListener('input',e=>{
    const input=e.target;
    if(!(input instanceof HTMLInputElement))return;
    values[input.name]=input.type==='checkbox'?input.checked:input.type==='range'?input.valueAsNumber:input.value;
    save();
  });
  function open(){if(!dialog.open){onPause(true);dialog.showModal();tabs.find(t=>t.tabIndex===0).focus();}}
  function close(){if(dialog.open){dialog.close();onPause(false);canvas.focus({preventScroll:true});}}
  const docRoot=document.documentElement;
  const enterScreen=docRoot.requestFullscreen||docRoot.webkitRequestFullscreen;
  const leaveScreen=document.exitFullscreen||document.webkitExitFullscreen;
  const displayButtons=[document.getElementById('toggleFullscreen'),document.getElementById('touchFullscreen')];
  const screenStatus=document.getElementById('fullscreenStatus');
  const fullscreenAvailable=Boolean(enterScreen&&leaveScreen&&(document.fullscreenEnabled??document.webkitFullscreenEnabled??true));
  const inFullscreen=()=>Boolean(document.fullscreenElement||document.webkitFullscreenElement);
  let screenBusy=false,wasFullscreen=inFullscreen(),manualExit=false,escapeTime=-Infinity;
  function syncFullscreen(){
    const active=inFullscreen(),label=active?'Sair da tela cheia':'Entrar em tela cheia';
    displayButtons[0].textContent=fullscreenAvailable?label:'Tela cheia indisponível';
    for(const button of displayButtons){
      button.disabled=!fullscreenAvailable;
      button.setAttribute('aria-label',fullscreenAvailable?label:'Tela cheia indisponível neste navegador');
      button.setAttribute('aria-pressed',String(active));
    }
    displayButtons[1].hidden=!fullscreenAvailable;
    if(wasFullscreen&&!active&&!manualExit&&performance.now()-escapeTime>350&&!dialog.open)open();
    wasFullscreen=active;if(!active)manualExit=false;
  }
  async function toggleFullscreen(){
    if(screenBusy||!fullscreenAvailable)return;
    screenBusy=true;screenStatus.textContent='';
    try{
      if(inFullscreen()){manualExit=true;await leaveScreen.call(document);}
      else await enterScreen.call(docRoot);
      if(dialog.open)close();
    }catch{manualExit=false;screenStatus.textContent='O navegador não permitiu mudar a exibição. Abra o jogo em uma aba própria para usar tela cheia.';if(!dialog.open)open();}
    finally{screenBusy=false;syncFullscreen();}
  }
  for(const button of displayButtons)button.addEventListener('click',toggleFullscreen);
  for(const name of ['fullscreenchange','webkitfullscreenchange'])document.addEventListener(name,syncFullscreen);
  syncFullscreen();
  document.getElementById('openSettings').addEventListener('click',open);
  document.getElementById('resumeGame').addEventListener('click',close);
  document.getElementById('resetSettings').addEventListener('click',()=>{Object.assign(values,defaults);save();});
  dialog.addEventListener('cancel',e=>{e.preventDefault();close();});
  window.addEventListener('keydown',e=>{
    if(e.altKey&&e.key==='Enter'){
      e.preventDefault();e.stopImmediatePropagation();if(!e.repeat)toggleFullscreen();return;
    }
    if(e.key!=='Escape')return;
    escapeTime=performance.now();
    e.preventDefault();e.stopImmediatePropagation();
    if(!e.repeat){if(dialog.open)close();else open();}
  },true);
  sync();
  return {values,get open(){return dialog.open;}};
}
