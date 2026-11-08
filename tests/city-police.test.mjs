import test from 'node:test';
import assert from 'node:assert/strict';
import {createCityPolice} from '../city-police.js';

function actor(name,x,z,role){return {name,role,g:{position:{x,y:0,z},rotation:{y:0}}};}
function fixture({copAt=35,witnessAt=3}={}){
  const officer=actor('Mara',0,copAt,'police'),witness=actor('Rui',0,witnessAt);
  const player={x:0,y:0,z:0},people=[officer,witness],control={sight:true,down:false,armed:false,t:0,fines:0};
  officer.g.rotation.y=witness.g.rotation.y=Math.PI;
  const moves=[],notices=[];
  const police=createCityPolice({people,position:()=>player,canSee:()=>control.sight,
    move:(person,to,dt,speed,mode)=>moves.push({person,to:{...to},mode}),notice:(a,n,line)=>notices.push(line),
    grounded:()=>true,threatening:()=>control.armed,down:()=>control.down,charge:amount=>{control.fines++;return Math.min(32,amount);}});
  function tick(seconds){
    for(let i=0;i<Math.round(seconds*20);i++){control.t+=.05;police.update(.05,control.t);}
  }
  return {police,control,player,officer,witness,tick,moves,notices};
}

test('a quiet unseen incident is ignored; a sound behind a wall gives a location but no identity',()=>{
  const f=fixture();f.control.sight=false;
  assert.equal(f.police.report('roubo',f.player,{},0),false);
  assert.equal(f.police.report('disparo',f.player,{loud:true},0),true);
  f.tick(4.8);assert.equal(f.police.snapshot().phase,'despacho');assert.equal(f.police.snapshot().identified,false);
  f.control.sight=true;f.officer.g.position.z=3;f.tick(.3);
  assert.equal(f.police.snapshot().phase,'busca');assert.equal(f.police.approaching(),null);
  f.tick(17);assert.equal(f.police.snapshot().phase,'livre');
});

test('interrupting the actual caller prevents dispatch; a uniformed eyewitness can still respond',()=>{
  const f=fixture();assert.equal(f.police.report('agressao',f.player,{victim:f.witness},0),true);
  assert.equal(f.police.snapshot().witness,'Rui');assert.equal(f.police.interrupt({}),false);
  assert.equal(f.police.interrupt(f.police.device.unit),true);
  f.tick(6);assert.equal(f.police.snapshot().phase,'livre');assert.equal(f.police.report('roubo',f.player,{},6),false);
  f.officer.g.position.z=4;f.police.report('roubo',f.player,{},6);
  assert.equal(f.police.snapshot().phase,'perseguicao');assert.equal(f.police.device,null);
});

test('the pursuit searches the last sighting, never the hidden player, then releases everyone',()=>{
  const f=fixture({copAt:12});f.police.report('agressao',f.player,{},0);f.tick(.2);
  f.control.sight=false;f.player.x=63;f.tick(2.3);
  assert.equal(f.police.snapshot().phase,'busca');assert.equal(f.police.snapshot().lastKnown.x,0);
  assert.ok(f.moves.filter(m=>m.mode==='busca').every(m=>m.to.x<7));
  f.tick(16);assert.equal(f.police.snapshot().phase,'livre');assert.equal(f.police.tradeBlocked,false);
  assert.equal(f.control.fines,0);
});

test('an identified suspect can surrender once in reach and regain control with limited funds',()=>{
  const f=fixture({copAt:2});f.police.report('disparo',f.player,{loud:true},0);f.tick(.2);
  assert.equal(f.police.approaching(),f.officer);assert.equal(f.police.surrender(),true);
  assert.equal(f.police.custody,true);assert.equal(f.police.surrender(),false);
  f.tick(2.2);assert.equal(f.police.custody,false);assert.equal(f.control.fines,1);
  assert.equal(f.police.snapshot().fine,32);f.tick(4);assert.equal(f.control.fines,1);
});

test('direct identification supersedes a pending call and medical recovery cannot leave an active arrest',()=>{
  const f=fixture();f.police.report('disparo',f.player,{loud:true},0);
  const old=f.police.device.unit;f.officer.g.position.z=2;f.police.report('agressao',f.player,{victim:f.officer},.2);
  assert.equal(f.police.interrupt(old),false);assert.equal(f.police.snapshot().level,2);
  f.control.down=true;f.tick(.1);assert.equal(f.police.snapshot().phase,'livre');assert.equal(f.police.tradeBlocked,false);
});

test('seeing a distant hit without seeing its source does not identify the shooter',()=>{
  const witness=actor('Rui',0,2),victim=actor('Bia',0,0),player={x:30,y:0,z:0};
  const city=createCityPolice({people:[witness,victim],position:()=>player,canSee:(_p,spot)=>spot.x<12,
    grounded:()=>true,move(){},notice(){},charge:()=>0});
  city.report('agressao',victim.g.position,{victim},1);city.update(.1,6);
  assert.equal(city.snapshot().identified,false);assert.equal(city.snapshot().lastKnown.x,0);
});

test('another neighborhood patrol must see the described suspect before taking over',()=>{
  const a=actor('A',0,1,'police'),b=actor('B',3,3,'police'),c=actor('C',64,0,'police');
  const player={x:0,y:0,z:0};let visible=true;
  const city=createCityPolice({people:[a,b,c],position:()=>player,canSee:()=>visible,grounded:()=>true,move(){},notice(){},charge:()=>0});
  city.report('roubo',player,{loud:true},0);visible=false;player.x=64;player.z=4;
  city.update(.1,2.5);assert.equal(city.snapshot().phase,'busca');
  assert.equal(city.snapshot().lastKnown.x,0);
  visible=true;city.update(.2,2.8);assert.equal(city.snapshot().lastKnown.x,64);
  assert.ok(city.snapshot().officers.some(p=>p.name==='C'));
});

test('a mobile patrol reports visible positions; losing radio contact leaves the last sighting and ends the search',()=>{
  const f=fixture({copAt:95,witnessAt:87});f.control.sight=false;
  assert(f.police.report('roubo',f.player,{cruiserWitness:true},0));
  f.player.x=21;assert(f.police.cruiserSight(f.player,.7));f.player.x=72;
  f.tick(3.1);assert.equal(f.police.snapshot().phase,'busca');assert.equal(f.police.order.point.x,21);
  f.tick(17);assert.equal(f.police.order,null);assert.equal(f.police.cruiserSight(f.player,21),false);
});
