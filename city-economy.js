// Shared by the physical shop, fish dispatch and bank terminals. Cash and the
// loadout are written together so that a reload never separates a sale from its gun.
const key='open-territory.carteira.v1';
const capital=320,ceiling=999999;
export const merchandise={
  pulse:{short:'Pulsor',pack:4,ammo:16},
  sidearm:{short:'P-9',price:180,buyback:105,pack:12,ammo:24},
  carbine:{short:'C-12',price:360,buyback:205,pack:20,ammo:40}
};
const orders=[2,3,1,4]; // Each fish and iron weight is half a kilogram.
export const cashText=value=>'R$ '+value;
export const weightText=units=>(units/2).toLocaleString('pt-BR')+' kg';
const money=v=>Number.isSafeInteger(v)&&v>=0;
export function createCityEconomy(combat,storage){
  let balance=capital,shipments=0,lastSaved;
  function snapshot(){return {balance,shipments,nextWeight:orders[shipments%orders.length]};}
  function record(){return JSON.stringify({v:1,balance,shipments,loadout:combat.exportLoadout()});}
  function restore(raw){
    try{
      if(!raw||raw===lastSaved)return;
      const d=JSON.parse(raw);
      if(d?.v!==1||!money(d.balance)||d.balance>ceiling||!money(d.shipments)||!d.loadout||typeof d.loadout!=='object')return;
      if(JSON.stringify(d.loadout)!==JSON.stringify(combat.exportLoadout()))combat.restoreLoadout(d.loadout);
      balance=d.balance;shipments=d.shipments;lastSaved=record();
    }catch{/* A corrupt or blocked browser store does not stop free play. */}
  }
  function checkpoint(){
    if(!storage)return;
    const raw=record();if(raw===lastSaved)return;
    try{storage.setItem(key,raw);lastSaved=raw;}catch{}
  }
  try{restore(storage?.getItem(key));}catch{}
  lastSaved=record();
  function quote(kind,id){
    const sku=merchandise[id];
    if(!sku)return null;
    const info=combat.weaponInfo(id),owned=combat.owns(id);
    if(kind==='buy'&&!owned&&sku.price)return {verb:'buy',label:'Comprar '+sku.short+' · '+cashText(sku.price),price:sku.price};
    if(kind==='sell'&&sku.buyback)return {verb:owned?'sell':'missing',label:(owned?'Vender ':'Avaliar ')+sku.short+(owned?' · '+cashText(sku.buyback):''),price:sku.buyback};
    if(kind!=='buy'||!owned)return null;
    const amount=Math.max(0,Math.min(sku.pack,info.maxReserve-info.reserve));
    const price=Math.ceil(sku.ammo*amount/sku.pack);
    return {verb:amount?'ammo':'full',amount,price,label:amount?'Repor '+sku.short+' · '+cashText(price):'Consultar '+sku.short};
  }
  function trade(kind,id){
    const q=quote(kind,id),sku=merchandise[id];
    if(!q)return {ok:false,line:'Este equipamento não está disponível.'};
    if(q.verb==='missing')return {ok:false,line:'Você não tem '+sku.short+' para revender. A recompra vale '+cashText(q.price)+'.'};
    if(q.verb==='full')return {ok:false,line:sku.short+': a sua reserva já está cheia. Saldo: '+cashText(balance)+'.'};
    if(q.verb==='sell'){
      if(!combat.remove(id))return {ok:false,line:'Não há equipamento para revender.'};
      balance=Math.min(ceiling,balance+q.price);checkpoint();
      return {ok:true,line:sku.short+' entregue com a munição. Você recebeu '+cashText(q.price)+'. Saldo: '+cashText(balance)+'.'};
    }
    if(balance<q.price)return {ok:false,line:'Faltam '+cashText(q.price-balance)+'. Seu saldo é '+cashText(balance)+'. O galpão da peixaria paga por lotes pesados.'};
    if(q.verb==='buy'){
      if(!combat.grant(id))return {ok:false,line:'Você já tem este equipamento.'};
      balance-=q.price;checkpoint();
      return {ok:true,line:sku.short+' comprada. '+(id==='sidearm'?'Tecla 2':'Tecla 3')+' ou botão Arma no toque. Saldo: '+cashText(balance)+'.'};
    }
    const n=combat.addAmmo(id,q.amount);
    if(!(n>0))return {ok:false,line:'Não há espaço para mais munição.'};
    const cost=Math.ceil(n*sku.ammo/sku.pack);balance-=cost;checkpoint();
    return {ok:true,line:'+'+n+(id==='pulse'?' cargas':' cartuchos')+' para '+sku.short+'. Pago: '+cashText(cost)+'. Saldo: '+cashText(balance)+'.'};
  }
  function dispatch(fish,weights){
    const target=orders[shipments%orders.length],next=weightText(target);
    if(fish!==target||weights!==target)return {ok:false,line:'Este caixote pede '+next+'. Escolha os peixes na caixa azul e iguale os pratos com os pesos da caixa de madeira.'};
    const payout=40+target*8;balance=Math.min(ceiling,balance+payout);shipments++;checkpoint();
    return {ok:true,line:'Lote de '+next+' recebido. +'+cashText(payout)+'. Saldo: '+cashText(balance)+'. Próximo caixote: '+weightText(orders[shipments%orders.length])+'.'};
  }
  function fine(amount){
    if(!Number.isFinite(amount)||amount<=0)return 0;
    const paid=Math.min(balance,Math.floor(amount));balance-=paid;checkpoint();return paid;
  }
  return {snapshot,quote,trade,dispatch,fine,checkpoint,onStorage(event){if(event.key===key)restore(event.newValue);}};
}
