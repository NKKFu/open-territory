// Re-render only text-bearing canvases from the legacy art source. No city export.
const fs = require('fs');
const path = require('path');
const http = require('http');
const {chromium} = require('playwright');
const root = path.resolve(__dirname, '../..');
const out = path.join(root, 'Unity/AssetLibrary/EnglishSigns');
const catalog = JSON.parse(fs.readFileSync(path.join(root, 'Unity/OpenTerritory/Assets/Resources/English.json'), 'utf8'));

async function main() {
  fs.mkdirSync(out, {recursive:true});
  const server = http.createServer((req,res) => {
    let relative = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    if (relative === '/') relative = '/index.html';
    const file = path.resolve(root, '.' + relative);
    if (!file.startsWith(root + path.sep) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {res.writeHead(404);res.end();return;}
    res.setHeader('Content-Type', file.endsWith('.js') ? 'text/javascript' : file.endsWith('.html') ? 'text/html' : file.endsWith('.png') ? 'image/png' : 'application/octet-stream');
    res.end(fs.readFileSync(file));
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const browser = await chromium.launch({headless:true,args:['--enable-unsafe-swiftshader']});
  try {
    const page = await browser.newPage({viewport:{width:900,height:600}});
    page.on('pageerror', e => console.error(e.message));
    await page.addInitScript(entries => {
      const map = new Map(entries.map(e=>[e.source.toLocaleLowerCase('en-US'),e.target]));
      const escape = s=>s.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
      const word = c=>/[\p{L}\p{N}_]/u.test(c);
      const regex = new RegExp(entries.map(e=>e.source).sort((a,b)=>b.length-a.length).map(k=>
        (word(k[0])?'(?<![\\p{L}\\p{N}_])':'')+escape(k)+(word(k.at(-1))?'(?![\\p{L}\\p{N}_])':'')).join('|'),'giu');
      window.__english = value => String(value).replace(regex, m => {
        const translated = map.get(m.toLocaleLowerCase('en-US'));
        return m === m.toUpperCase() && /[\p{L}]/u.test(m) ? translated.toUpperCase() : translated;
      });
      const proto = CanvasRenderingContext2D.prototype;
      const properties = ['fillStyle','strokeStyle','font','textAlign','textBaseline','lineWidth','lineCap','lineJoin','miterLimit','shadowBlur','shadowColor','shadowOffsetX','shadowOffsetY','globalAlpha','globalCompositeOperation','imageSmoothingEnabled','direction','filter','letterSpacing','fontKerning'];
      const methods = ['fillRect','strokeRect','clearRect','beginPath','closePath','moveTo','lineTo','rect','roundRect','arc','arcTo','ellipse','bezierCurveTo','quadraticCurveTo','fill','stroke','clip','save','restore','rotate','scale','translate','transform','setTransform','resetTransform','setLineDash','drawImage','fillText','strokeText','putImageData'];
      for (const name of methods) {
        const original = proto[name]; if (!original) continue;
        proto[name] = function(...args) {
          if (this.__englishShadow) return original.apply(this,args);
          const canvas = this.canvas;
          if (!canvas.__englishCanvas) {
            canvas.__englishCanvas = document.createElement('canvas');
            canvas.__englishContext = canvas.__englishCanvas.getContext('2d');
            canvas.__englishContext.__englishShadow = true; canvas.__englishTexts = [];
          }
          const shadow = canvas.__englishContext, mirror = canvas.__englishCanvas;
          if (mirror.width !== canvas.width) mirror.width = canvas.width;
          if (mirror.height !== canvas.height) mirror.height = canvas.height;
          for (const key of properties) if (key in this) shadow[key] = this[key];
          let translated = args;
          if (name === 'fillText' || name === 'strokeText') {
            canvas.__englishTexts.push(String(args[0]));
            translated = [window.__english(args[0]),...args.slice(1)];
          } else if (name === 'drawImage' && args[0]?.__englishCanvas) translated = [args[0].__englishCanvas,...args.slice(1)];
          original.apply(shadow,translated);
          return original.apply(this,args);
        };
      }
    }, catalog.entries);
    await page.route('**/game.js*', async route => {
      let source = fs.readFileSync(path.join(root,'game.js'),'utf8');
      const index = source.lastIndexOf('animate();');
      source = source.slice(0,index) + `
window.__englishDump=()=>{
 const used=new Set(); scene.traverse(o=>{for(const m of (Array.isArray(o.material)?o.material:[o.material]))if(m?.map?.image?.__englishTexts?.length)used.add(m.map.image);});
 return [...used].map((canvas,i)=>({id:i,texts:canvas.__englishTexts,original:canvas.toDataURL('image/png'),english:canvas.__englishCanvas.toDataURL('image/png')}));
};\n` + source.slice(index+10);
      await route.fulfill({status:200,contentType:'text/javascript',body:source});
    });
    await page.goto('http://127.0.0.1:'+server.address().port+'/',{waitUntil:'networkidle',timeout:120000});
    await page.waitForFunction(()=>window.__englishDump,{timeout:120000});
    const data = await page.evaluate(()=>window.__englishDump());
    const manifest = [];
    for (const item of data) {
      const original = 'original-'+item.id+'.png', english = 'english-'+item.id+'.png';
      fs.writeFileSync(path.join(out,original),Buffer.from(item.original.split(',')[1],'base64'));
      fs.writeFileSync(path.join(out,english),Buffer.from(item.english.split(',')[1],'base64'));
      manifest.push({id:item.id,texts:item.texts,original,english});
    }
    fs.writeFileSync(path.join(out,'canvases.json'),JSON.stringify(manifest,null,2)+'\n');
    console.log(JSON.stringify({textCanvases:manifest.length,texts:[...new Set(manifest.flatMap(x=>x.texts))]},null,2));
  } finally {await browser.close();await new Promise(resolve=>server.close(resolve));}
}
main().catch(e=>{console.error(e);process.exit(1);});
