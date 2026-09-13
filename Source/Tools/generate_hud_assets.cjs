// Build-time vector artwork. Requires sharp; generated PNGs are embedded in the mod.
// No rasterization, disk reads or ring geometry generation occurs in the frame loop.
const fs = require('fs');
const path = require('path');
const sharp = require('sharp');
const root = path.resolve(__dirname, '..');
const out = path.join(root, 'DVSurvival.Game', 'Assets');
const svg = (w, h, body) => `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">${body}</svg>`;
const defs = `<defs>
<linearGradient id="metal" x2="0" y2="1"><stop stop-color="#d2ac70"/><stop offset=".45" stop-color="#786044"/><stop offset=".7" stop-color="#392e23"/><stop offset="1" stop-color="#b59462"/></linearGradient>
<linearGradient id="plate" x2="0" y2="1"><stop stop-color="#211e19" stop-opacity=".94"/><stop offset="1" stop-color="#090b0b" stop-opacity=".94"/></linearGradient>
<linearGradient id="thermal"><stop stop-color="#417a9c"/><stop offset=".38" stop-color="#7caaa9"/><stop offset=".52" stop-color="#a59876"/><stop offset=".72" stop-color="#9b655f"/><stop offset="1" stop-color="#b33935"/></linearGradient>
</defs>`;
function panel(w, h) {
  const outline = `M12 2 H${w-12} L${w-2} 12 V${h-12} L${w-12} ${h-2} H12 L2 ${h-12} V12 Z`;
  let body = `<path d="${outline}" fill="url(#plate)" stroke="#050606" stroke-width="4"/><path d="${outline}" fill="none" stroke="url(#metal)" stroke-width="1.3"/><path d="M14 5 H${w-14} L${w-5} 14 V${h-14} L${w-14} ${h-5} H14 L5 ${h-14} V14 Z" fill="none" stroke="#725b3e" stroke-opacity=".48"/>`;
  for (const [x,y,sx,sy] of [[8,8,1,1],[w-8,8,-1,1],[8,h-8,1,-1],[w-8,h-8,-1,-1]]) {
    body += `<g transform="translate(${x} ${y}) scale(${sx} ${sy})"><path d="M0 12 V3 Q0 0 3 0 H12" fill="none" stroke="#b89968" stroke-width="1.1"/><circle r="2.1" fill="#a38459" stroke="#1b1610" stroke-width="1"/><path d="M-1 1 L1 -1" stroke="#30261a" stroke-width=".7"/></g>`;
  }
  return body;
}
function dial() {
  let b = '<circle cx="48" cy="48" r="46" fill="#090b0a" fill-opacity=".96" stroke="#030403" stroke-width="3"/><circle cx="48" cy="48" r="44" fill="none" stroke="url(#metal)" stroke-width="1.3"/><circle cx="48" cy="48" r="37" fill="none" stroke="#403b30" stroke-width="4"/><circle cx="48" cy="48" r="32" fill="none" stroke="#6c583d" stroke-opacity=".45"/>';
  for(let i=0;i<24;i++) { const a=i*Math.PI/12; const p=r=>[48+Math.sin(a)*r,48-Math.cos(a)*r]; const a1=p(40),a2=p(i%3===0?43:41.5); b+=`<path d="M${a1} L${a2}" stroke="#ad8e5c" stroke-opacity=".65" stroke-width="1"/>`; }
  return b;
}
function ring(value, color='white') {
  const c=2*Math.PI*37;
  return `<circle cx="48" cy="48" r="37" fill="none" stroke="${color}" stroke-width="4" stroke-linecap="${value?'round':'butt'}" stroke-dasharray="${c*value/100} ${c}" transform="rotate(-90 48 48)"/>`;
}
const icons = [
 '<path d="M32 53 C27 47 9 35 9 22 C9 8 25 7 32 21 C39 7 55 8 55 22 C55 35 37 48 32 53Z"/>',
 '<g fill="none" stroke="currentColor" stroke-width="3.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 7 V21 Q12 29 20 29 Q28 29 28 21 V7 M20 7 V22 M20 29 V55"/><path d="M44 28 V55" stroke-width="4.6"/></g><ellipse cx="44" cy="17" rx="8" ry="12" fill="currentColor"/>',
 '<path d="M32 5 C28 17 13 30 13 41 A19 19 0 0 0 51 41 C51 30 36 17 32 5Z"/><path d="M20 41 Q20 48 28 50" fill="none" stroke="#fff" stroke-opacity=".35" stroke-width="2" stroke-linecap="round"/>',
 '<path d="M44 8 A25 25 0 1 0 56 43 A24 24 0 0 1 44 8Z"/>',
 '<g fill="none" stroke="currentColor" stroke-width="2.8"><path d="M26 39 V10 A6 6 0 0 1 38 10 V39 A12 12 0 1 1 26 39Z"/><path d="M32 15 V44" stroke-width="3"/><circle cx="32" cy="49" r="5" fill="currentColor"/></g>',
 '<g fill="none" stroke="currentColor" stroke-width="2.5" stroke-linejoin="round"><path d="M32 7 L59 54 H5Z"/><path d="M32 23 V37" stroke-width="3"/><circle cx="32" cy="45" r="1.5" fill="currentColor"/></g>',
 '<path d="M24 9 L40 9 L36 20 C51 29 56 47 47 54 H17 C8 47 13 29 28 20Z"/><path d="M24 21 H40" fill="none" stroke="#111" stroke-width="3"/>',
 '<path d="M32 10 L51 32 L32 54 L13 32Z" fill="#b49a80" stroke="#edd0a0" stroke-width="3"/><path d="M32 15 L46 32 L32 49 L18 32Z" fill="#67525f"/>'
];
async function write(name,w,h,body) {
 await sharp(Buffer.from(svg(w,h,defs+body))).png().toFile(path.join(out,name+'.png'));
}
async function main() {
 fs.mkdirSync(out,{recursive:true});
 await write('panel',128,128,panel(128,128));
 await write('dial',96,96,dial());
 await write('icons',512,64,icons.map((icon,i)=>`<g transform="translate(${i*64} 0)" fill="white" color="white">${icon}</g>`).join(''));
 await write('thermal',256,8,'<rect width="256" height="8" fill="url(#thermal)"/>');
 await write('rings',1056,960,Array.from({length:101},(_,i)=>`<g transform="translate(${i%11*96} ${Math.floor(i/11)*96})">${ring(i)}</g>`).join(''));
 // Flat design proof: same asset geometry, positions and sizes as the 1672x941 runtime HUD.
 const colors=['#c26a5a','#d29a47','#43abd0','#8d79cf'];
 const labels=['Здоровье','Сытость','Вода','Сон'];
 let proof=defs+'<rect width="1672" height="941" fill="#29343a"/><text x="28" y="48" fill="#d0b48b" font-family="Georgia" font-size="20">Survival Needs · макет HUD 0.4.0</text>';
 const text=(x,y,s,size=12,color='#e4dccc')=>`<text x="${x}" y="${y}" fill="${color}" font-family="Georgia" font-size="${size}">${s}</text>`;
 function at(x,y,b) {proof+=`<g transform="translate(${x} ${y})">${b}</g>`;}
 [9,9,9,84].forEach((v,i)=>{at(28+i*86,737,panel(80,112)); at(28+i*86,735,`<g transform="scale(.833333)">${dial()}${ring(v,colors[i])}</g>`); at(52+i*86,759,`<g transform="scale(.5)" fill="${colors[i]}" color="${colors[i]}">${icons[i]}</g>`); proof+=text(62+i*86,806,v+'%'); proof+=text(68+i*86-labels[i].length*3,835,labels[i]);});
 at(28,673,panel(338,50)); at(40,684,`<g transform="scale(.45)" color="#d2ac70">${icons[5]}</g>`); proof+=text(82,693,'Критическое состояние',14,'#d2ac70')+text(82,710,'Здоровье · сытость · вода',12);
 at(28,873,panel(338,36)); proof+=text(43,896,'Воздух: −3.1 °C   Ощущается: −7.6 °C');
 at(680,855,panel(312,54));at(692,862,`<g transform="scale(.58)" color="#d2ac70">${icons[4]}</g>`);proof+=text(730,877,'Температура тела   35.5 °C',14,'#d2ac70');proof+='<rect x="730" y="888" width="244" height="6" fill="url(#thermal)"/>';at(781,881,`<g transform="scale(.28)">${icons[7]}</g>`);
 at(1388,855,panel(256,54));proof+=text(1403,886,'F6',17,'#d2ac70')+text(1440,877,'Инвентарь / Магазин',14)+text(1440,896,'Нажмите, чтобы открыть',11,'#b8a58a');
 const previewDir=path.join(root,'artifacts','previews'); fs.mkdirSync(previewDir,{recursive:true});
 await sharp(Buffer.from(svg(1672,941,proof))).png().toFile(path.join(previewDir,'hud-0.4.0.png'));
 console.log('HUD artwork and design proof generated.');
}
main().catch(e=>{console.error(e);process.exit(1);});
