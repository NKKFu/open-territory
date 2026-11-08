import test from 'node:test';
import assert from 'node:assert/strict';
import {createCombat} from '../city-combat.js';
import {createCityEconomy} from '../city-economy.js';
function fixture(store=new Map()){
  const combat=createCombat({player:{}});
  const storage={getItem:k=>store.get(k),setItem:(k,v)=>store.set(k,v)};
  return {store,combat,wallet:createCityEconomy(combat,storage)};
}
test('purchase, actual ammo use, reload and resale survive reopening without duplicating money or equipment',()=>{
  const a=fixture(),{wallet,combat}=a;
  assert.equal(wallet.trade('buy','carbine').ok,false);
  assert.equal(wallet.snapshot().balance,320);
  assert.equal(wallet.trade('buy','sidearm').ok,true);
  assert.equal(combat.select('sidearm'),true);combat.aim(true);combat.trigger(true);combat.trigger(false);
  assert.equal(combat.reload(),true);combat.update(2);wallet.checkpoint();
  const b=fixture(a.store);
  assert.equal(b.wallet.snapshot().balance,140);assert.equal(b.combat.equipped,null);
  assert.equal(b.combat.weaponInfo('sidearm').reserve,15);
  assert.equal(b.wallet.quote('buy','sidearm').verb,'ammo');
  assert.equal(b.wallet.trade('sell','sidearm').ok,true);assert.equal(b.wallet.snapshot().balance,245);
  assert.equal(b.wallet.trade('sell','sidearm').ok,false);assert.equal(fixture(a.store).combat.owns('sidearm'),false);
});
test('partial ammunition is priced by units delivered; full packs and fines cannot overdraw',()=>{
  const {wallet,combat}=fixture();
  combat.addAmmo('pulse',3);
  assert.deepEqual([wallet.quote('buy','pulse').price,wallet.quote('buy','pulse').amount],[4,1]);
  wallet.trade('buy','pulse');assert.equal(wallet.snapshot().balance,316);
  assert.equal(wallet.trade('buy','pulse').ok,false);assert.equal(wallet.trade('sell','pulse').ok,false);
  assert.equal(wallet.fine(500),316);assert.equal(wallet.fine(200),0);assert.equal(wallet.snapshot().balance,0);
});
test('only a requested, balanced fish lot pays; repeating the receipt or reloading cannot claim it twice',()=>{
  const {wallet,store}=fixture();
  for(const pair of [[1,1],[2,0],[2,3]])assert.equal(wallet.dispatch(...pair).ok,false);
  assert.equal(wallet.dispatch(2,2).ok,true);assert.equal(wallet.snapshot().balance,376);
  assert.equal(wallet.dispatch(2,2).ok,false);
  const b=fixture(store);assert.equal(b.wallet.snapshot().nextWeight,3);assert.equal(b.wallet.dispatch(2,2).ok,false);
  assert.equal(b.wallet.trade('buy','carbine').ok,true);assert.equal(b.wallet.snapshot().balance,16);
});
test('a clean background tab cannot overwrite a newer wallet when closing; malformed saves keep free play available',()=>{
  const {wallet,store}=fixture(),b=fixture(store);
  wallet.trade('buy','sidearm');b.wallet.checkpoint();
  assert.equal(fixture(store).wallet.snapshot().balance,140);
  const [key,value]=[...store][0];b.wallet.onStorage({key,newValue:value});
  assert.equal(b.wallet.snapshot().balance,140);
  for(const raw of ['{',JSON.stringify({v:1,balance:-23,shipments:0,loadout:{}})]){
    const bad=fixture(new Map([[key,raw]]));assert.equal(bad.wallet.snapshot().balance,320);assert.equal(bad.combat.owns('pulse'),true);
  }
});
