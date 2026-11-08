// Reproducible audit of the real constructed world, in an isolated browser.
// Run: node tests/external-solids-audit.mjs
import {chromium} from 'playwright';
import {createServer} from 'node:http';
import {readFile,mkdir,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import assert from 'node:assert/strict';

const root=fileURLToPath(new URL('../',import.meta.url));
const output=path.join(root,'reports','external-solids');
await mkdir(output,{recursive:true});
const server=createServer(async(req,res)=>{
  try{
    const pathname=decodeURIComponent(new URL(req.url,'http://localhost').pathname);
    const target=path.resolve(root,'.'+(pathname==='/'?'/index.html':pathname));
    if(!target.startsWith(root)){res.writeHead(403).end();return;}
    res.setHeader('Content-Type',target.endsWith('.js')?'text/javascript':target.endsWith('.css')?'text/css':target.endsWith('.html')?'text/html':'application/octet-stream');
    res.end(await readFile(target));
  }catch{res.writeHead(404).end();}
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
let browser;
try{
  browser=await chromium.launch({headless:true,args:['--use-angle=swiftshader','--enable-unsafe-swiftshader']});
  const page=await browser.newPage({viewport:{width:1100,height:760}});
  const errors=[];
  page.on('pageerror',error=>errors.push(String(error)));
  await page.route('**/game.js',async route=>{
    const source=await readFile(path.join(root,'game.js'),'utf8');
    await route.fulfill({contentType:'text/javascript',body:source+`
      window.externalAudit={
        probe:(x,z,r=.08,swimmer=false)=>collides(x,z,r,null,2.4,swimmer),
        solids:()=>solids.filter(s=>s.active).map(s=>({...s})),
        ground:groundHeight,grade:cityElevation,
        stand:(x,z,heading=0)=>{vehicle=null;sitting=null;player.position.set(x,groundHeight(x,z),z);velocity.set(0,0,0);yaw=targetYaw=heading;pitch=targetPitch=.5;},
        view:(x,z)=>{player.position.set(x,groundHeight(x,z),z);yaw=targetYaw=0;pitch=targetPitch=.65;},
        position:()=>({x:player.position.x,y:player.position.y,z:player.position.z})
      };`});
  });
  await page.goto(`http://127.0.0.1:${server.address().port}/?debug&near=farol`);
  await page.waitForFunction(()=>!!window.externalAudit,{timeout:60000});
  const results=await page.evaluate(()=>{
    const api=window.externalAudit,rows=[];
    const check=(label,x,z,blocked,r=.08)=>rows.push({label,x,z,r,expected:blocked,actual:api.probe(x,z,r)});
    // Eight kiosks: poles and occupied furniture, plus walkable roofed bays.
    [-105,-75,-45,-15,15,45,75,105].forEach((x,i)=>{
      const kind=i%3;
      check(`beach ${i}: support`,x+(kind===0?0:2.46),-160+(kind===0?0:1.65),true);
      if(kind===0){check(`beach ${i}: lounger`,x+1.81,-159.8,true);check(`beach ${i}: aisle`,x+.8,-159,false);}
      else{
        check(`beach ${i}: roofed passage`,x,-158.9,false);
        check(`beach ${i}: counter/shelf`,x,kind===1?-159.7:-160.67,true);
        if(kind===2)check(`beach ${i}: beside thin shelf`,x,-160.18,false);
      }
    });
    check('small plaza kiosk body',104,112,true);
    check('small plaza kiosk side',106,112,false);
    for(const x of [101,107])check(`sandbox support ${x}`,x,97,true);
    check('sandbox middle passage',104,97,false);
    for(const x of [-3,3])for(let z=-176;z<=-150;z+=6)check(`pier mooring ${x}/${z}`,x,z,true);
    for(const z of [-177,-172,-166,-155])check(`pier center ${z}`,0,z,false,.42);
    check('sea outside pier stays closed to land traffic',8,-170,true,.42);
    rows.push({label:'swimmers can reach open water outside the pier',x:8,z:-170,r:.42,expected:false,actual:api.probe(8,-170,.42,true)});
    rows.push({label:'the pier post remains solid for swimmers',x:3,z:-170,r:.42,expected:true,actual:api.probe(3,-170,.42,true)});
    for(const x of [-180,-140,-100,-60,60,100,140,180]){
      const z=155+12*Math.sin(x/39)+23;
      check(`garden house ${x}`,x,z,true);
      check(`garden fence ${x}`,x+10,z-6,true);
      check(`garden gate ${x}`,x,z-9,false);
    }
    check('pond center',0,182,true);
    check('pond west lobe',-11.5,182,true);
    check('pond dry NE corner',10.8,186.8,false);
    check('pond dry NW corner',-10.8,186.8,false);
    check('pond dry SE corner',10.8,177.2,false);
    check('fountain basin',-36,36,true);
    check('popcorn cart',-47,53,true);
    check('ice cream body',-57,53,true);
    for(const [x,z] of [[-62,62],[10,62],[82,-10],[-82,-62]])check(`street sign pole ${x}/${z}`,x,z,true);
    for(const [x,z] of [[30,47.4],[-110,47.4],[111,47.4]])check(`ATM ${x}`,x,z,true);
    for(const [x,z] of [[-58.5,57],[86,21],[-86,-90]])check(`phone ${x}`,x,z,true);
    check('lighthouse',-32,-158,true);
    check('terrain gate',149,-103,false,.42);
    // Independent polygon test: sample wet and dry points, ignoring only a
    // 15 cm boundary band (10 cm inscribed collider strips plus probe radius).
    const outline=Array.from({length:14},(_,i)=>({x:12*Math.sin(i*Math.PI/7),z:182+6*Math.cos(i*Math.PI/7)}));
    let contourSamples=0;const contourFailures=[];
    for(let x=-12.8;x<=12.8;x+=.2)for(let z=175.2;z<=188.8;z+=.2){
      let inside=false,distance=Infinity;
      for(let i=0,j=13;i<14;j=i++){
        const a=outline[i],b=outline[j],vx=b.x-a.x,vz=b.z-a.z;
        const t=Math.max(0,Math.min(1,((x-a.x)*vx+(z-a.z)*vz)/(vx*vx+vz*vz)));
        distance=Math.min(distance,Math.hypot(x-a.x-t*vx,z-a.z-t*vz));
        if((a.z>z)!==(b.z>z)&&x<(b.x-a.x)*(z-a.z)/(b.z-a.z)+a.x)inside=!inside;
      }
      if(distance<.15)continue;
      contourSamples++;
      if(api.probe(x,z,.005)!==inside)contourFailures.push({x,z,inside});
    }
    return {rows,contourSamples,contourFailures,sandboxHeight:api.ground(104,97),sandboxGrade:api.grade(97),activeSolids:api.solids().length};
  });
  // Real W key through normal movement, approaching the formerly permeable kiosk.
  await page.evaluate(()=>window.externalAudit.stand(104,115,0));
  const start=await page.evaluate(()=>window.externalAudit.position());
  await page.keyboard.down('w');await page.waitForTimeout(1800);await page.keyboard.up('w');
  const end=await page.evaluate(()=>window.externalAudit.position());
  results.keyboard={start,end};
  for(const [name,x,z] of [['small-plaza',104,116],['shore-kiosks',45,-156],['pond',0,191],['pier',0,-164]]){
    await page.evaluate(([x,z])=>window.externalAudit.view(x,z),[x,z]);
    await page.waitForTimeout(550);
    await page.screenshot({path:path.join(output,`${name}.png`)});
  }
  results.errors=errors;
  await writeFile(path.join(output,'results.json'),JSON.stringify(results,null,2)+'\n');
  const failures=results.rows.filter(row=>row.expected!==row.actual);
  console.log(JSON.stringify({checks:results.rows.length,failures,contourSamples:results.contourSamples,contourFailures:results.contourFailures,keyboard:results.keyboard,sandboxHeight:results.sandboxHeight,errors},null,2));
  assert.deepEqual(failures,[]);
  assert.deepEqual(results.contourFailures,[]);
  assert.deepEqual(errors,[]);
  assert.ok(Math.abs(results.sandboxHeight-results.sandboxGrade-.56)<1e-6,'the sand bed must sit .56 above the local plaza grade');
  assert.ok(end.z<start.z-.3&&end.z>=113.06,'normal W movement must approach and stop at kiosk front');
}finally{
  await browser?.close();
  await new Promise(resolve=>server.close(resolve));
}
