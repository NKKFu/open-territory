const fs=require('fs');
const path=require('path');
const crypto=require('crypto');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../..');
const output=path.join(root,'Unity/OpenTerritory/Assets');
async function main(){
 const browser=await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
 const page=await browser.newPage({viewport:{width:900,height:600}});
 page.on('pageerror',e=>console.error(e.stack));
 await page.route('**/*.js*',async route=>{
  const filename=new URL(route.request().url()).pathname.slice(1);
  const file=path.join(root,filename);if(!fs.existsSync(file))return route.continue();
  let source=fs.readFileSync(file,'utf8');
  if(filename==='surface-sampler.js')source=source.replace('return {addRectangle,addTriangle,heightAt};','return {addRectangle,addTriangle,heightAt,_surfaces:()=>[...new Set([...cells.values()].flat())]};');
  if(filename==='fish-market.js')source=source.replace('return {\n    worker,','return {\n    exportData:()=>({beam,pans,load,weights,targets}),worker,');
  if(filename==='street-cinema.js')source=source.replace('return {device,toggle,update,','return {exportData:()=>({screen,projector,spools}),device,toggle,update,');
  if(filename==='practice-range.js')source=source.replace('return {impact,update,','return {exportData:()=>({targets}),impact,update,');
  if(filename==='marine-art.js')source=source.replace('return {buildBoat,buoy,update};','return {buildBoat,buoy,update,exportMaterials:()=>({surfaceMaterial,farMaterial})};');
  if(filename.includes('harbor')){
   source=source.replace('return {update,advance(){','return {mesh,update,advance(){');
   source=source.replace('return {network,hack,update,','return {exportData:()=>({crate,cables,cargoSolid,platter,sock,mural}),network,hack,update,');
  }
  if(filename==='game.js'){
   source=source.replace('rider.userData.pedal=phase=>{','rider.userData.unityLegs=legs;rider.userData.pedal=phase=>{');
   const index=source.lastIndexOf('animate();');
   source=source.slice(0,index)+fs.readFileSync(path.join(__dirname,process.argv.includes('--probe')?'export-scene.js':'export-world.js'),'utf8')+'\n'+source.slice(index+10);
  }
  await route.fulfill({status:200,contentType:'text/javascript',body:source});
 });
 await page.goto('http://127.0.0.1:8879/',{waitUntil:'networkidle',timeout:120000});
 await page.waitForFunction(()=>window.__unityExport,{timeout:120000});
 const data=await page.evaluate(()=>window.__unityExport());
 if(process.argv.includes('--probe')){console.log(JSON.stringify(data,null,2));await browser.close();return;}
 data.world.sourceHash=crypto.createHash('sha256').update(fs.readdirSync(root).filter(n=>n.endsWith('.js')).sort().map(n=>fs.readFileSync(path.join(root,n))).join('\n')).digest('hex');
 fs.mkdirSync(path.join(output,'Imported/Textures'),{recursive:true});
 for(const [name,url]of Object.entries(data.textures))fs.writeFileSync(path.join(output,'Imported/Textures',name+'.png'),Buffer.from(url.split(',')[1],'base64'));
 fs.writeFileSync(path.join(output,'Resources/world.json'),JSON.stringify(data.world));
 fs.writeFileSync(path.join(output,'Imported/visual.json'),JSON.stringify(data.visual));
 const report={sourceHash:data.world.sourceHash,nodes:data.visual.nodes.length,geometries:data.visual.geometries.length,materials:data.visual.materials.length,textures:Object.keys(data.textures).length,floors:data.visual.floors.length,residents:data.world.residents.length,vehicles:data.world.vehicles.length,boats:data.world.boats.length,solids:data.world.solids.length};
 fs.writeFileSync(path.join(root,'Unity/Reports/export.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));await browser.close();
}
main().catch(e=>{console.error(e);process.exit(1);});
