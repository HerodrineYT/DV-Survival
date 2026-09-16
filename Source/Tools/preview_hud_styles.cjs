// Render recorded production layout commands offline. Fonts/Unity blending may differ in-game.
const fs=require('fs'),path=require('path'),sharp=require('sharp');
const dir=path.resolve(__dirname,'../artifacts/previews/hud-styles');
const assets=path.resolve(__dirname,'../DVSurvival.Game/Assets');
const suffix=process.argv.includes('--alert')?'-alert':'';
const cache=new Map();
const esc=s=>String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/"/g,'&quot;');
const rgb=c=>`rgb(${Math.round(c.r*255)},${Math.round(c.g*255)},${Math.round(c.b*255)})`;
async function texture(name,crop,color) {
  const key=JSON.stringify([name,crop,color]);if(cache.has(key))return cache.get(key);
  let s=sharp(path.join(assets,name+'.png'));if(crop)s=s.extract(crop);
  let data;
  if(color){const r=await s.ensureAlpha().raw().toBuffer({resolveWithObject:true});
    for(let i=0;i<r.data.length;i+=4){r.data[i]*=color.r;r.data[i+1]*=color.g;r.data[i+2]*=color.b;r.data[i+3]*=color.a;}
    data=await sharp(r.data,{raw:r.info}).png().toBuffer();
  }else data=await s.png().toBuffer();
  const uri='data:image/png;base64,'+data.toString('base64');cache.set(key,uri);return uri;
}
const img=(uri,r)=>`<image x="${r.x}" y="${r.y}" width="${r.width}" height="${r.height}" href="${uri}" preserveAspectRatio="none"/>`;
const frame=v=>({left:(v%11)*96,top:Math.floor(v/11)*96,width:96,height:96});
async function render(c,i){const r=c.rect;
  switch(c.kind){
    case 'solid':return `<rect x="${r.x}" y="${r.y}" width="${r.width}" height="${r.height}" fill="${rgb(c.color)}" opacity="${c.color.a}"/>`;
    case 'text':{const center=c.style.align==='center',colored=/^<color=(#[0-9A-Fa-f]{6})>(.*)<\/color>$/.exec(c.text);return `<clipPath id="t${i}"><rect x="${r.x}" y="${r.y}" width="${r.width}" height="${r.height}"/></clipPath><text clip-path="url(#t${i})" x="${r.x+(center?r.width/2:0)}" y="${r.y+r.height/2+c.style.size*.34}" font-family="${c.style.font}" font-size="${c.style.size}" font-weight="${c.style.size===13?'bold':'normal'}" text-anchor="${center?'middle':'start'}" fill="${colored?colored[1]:'#edf5ff'}">${esc(colored?colored[2]:c.text)}</text>`;}
    case 'icon':return img(await texture('icons',{left:c.index*64,top:0,width:64,height:64},c.color),r);
    case 'thermal':return img(await texture('thermal'),r);
    case 'ring':case 'dial':return img(await texture(c.kind==='dial'?'dial':'rings',c.kind==='dial'?null:frame(100),c.kind==='dial'?null:{r:.16,g:.2,b:.25,a:1}),r)+img(await texture('rings',frame(Math.round(c.value*100)),c.color),r);
    case 'plate':{
      let out='';const xs=[0,16,112,128], ys=xs;
      const dx=[r.x,r.x+16,r.x+r.width-16,r.x+r.width],dy=[r.y,r.y+16,r.y+r.height-16,r.y+r.height];
      for(let y=0;y<3;y++)for(let x=0;x<3;x++)out+=img(await texture('panel',{left:xs[x],top:ys[y],width:xs[x+1]-xs[x],height:ys[y+1]-ys[y]}),{x:dx[x],y:dy[y],width:dx[x+1]-dx[x],height:dy[y+1]-dy[y]});return out;
    }
  }return '';
}
(async()=>{
 for(const lang of ['ru','en']){
  const names=lang==='ru'?['Кольца','Полосы','Строка','Минимум','Латунь']:['Rings','Bars','Ribbon','Minimal','Brass'];
  const positions=[[24,108],[512,108],[24,728],[24,430],[512,430]],heights=[172,188,74,124,192];
  let svg='<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="920"><defs><linearGradient id="bg" x2="0" y2="1"><stop stop-color="#233440"/><stop offset="1" stop-color="#111e28"/></linearGradient></defs><rect width="1024" height="920" fill="url(#bg)"/>';
  svg+=`<text x="28" y="40" fill="#f1f4f8" font-family="Arial" font-size="24">DVSurvival · ${lang==='ru'?'5 компактных интерфейсов':'5 compact HUD layouts'}</text><text x="28" y="68" fill="#abb9c7" font-family="Arial" font-size="14">${lang==='ru'?'Офлайн-превью по координатам мода · увеличено в 1,4 раза · не скриншот игры':'Offline production layout preview · 1.4× enlargement · not an in-game screenshot'}</text>`;
  for(let k=0;k<5;k++){
    const [x,y]=positions[k];svg+=`<text x="${x+5}" y="${y+20}" font-family="Arial" font-size="18" fill="#d4dee8">0${k+1} / ${names[k]}</text>`;
    svg+=`<g transform="translate(${x-28},${y+38-(240-heights[k])*1.4}) scale(1.4)">`;
    const commands=JSON.parse(fs.readFileSync(path.join(dir,`style-${k}-${lang}${suffix}.json`),'utf8'));
    for(let i=0;i<commands.length;i++)svg+=await render(commands[i],`${k}-${i}`);svg+='</g>';
  }
  svg+='</svg>';await sharp(Buffer.from(svg)).png().toFile(path.join(dir,`overview-${lang}${suffix}.png`));
  const legacyPath=path.join(dir,`style-5-${lang}${suffix}.json`);
  if(fs.existsSync(legacyPath)){
    let legacy='<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="720"><rect width="1280" height="720" fill="#233440"/><text x="28" y="44" font-family="Arial" font-size="22" fill="white">Classic / original HUD · offline preview · 1×</text>';
    const commands=JSON.parse(fs.readFileSync(legacyPath,'utf8'));
    for(let i=0;i<commands.length;i++)legacy+=await render(commands[i],`legacy-${i}`);
    await sharp(Buffer.from(legacy+'</svg>')).png().toFile(path.join(dir,`classic-${lang}${suffix}.png`));
  }
 }
 console.log('Rendered both HUD layout overview images.');
})().catch(e=>{console.error(e);process.exit(1)});
