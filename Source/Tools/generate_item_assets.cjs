// Build-time artwork only. Runtime uses one opaque atlas and one merged mesh per item.
const fs = require('fs');
const path = require('path');
const sharp = require('sharp');
const palette = ['#b7aa83','#374d43','#3185a0','#e3decc','#424847','#9ba4a2','#733d28','#ab3932','#d17b36','#272c2a'];
const labels = [
  ['#e1d4ad','#374d43','ПАЁК','ПОЛЕВОЙ РАЦИОН','ГОТОВ К УПОТРЕБЛЕНИЮ','450 г', '<path d="M105 111h46v25h-46zm-8-8h62v8H97zm10-9h42v8h-42z"/>'],
  ['#e8e6d8','#246e8c','ВОДА','ПИТЬЕВАЯ','ЧИСТАЯ • БЕЗ ГАЗА','500 мл','<path d="M128 84c-8 15-23 29-23 42a23 23 0 0046 0c0-13-15-27-23-42z"/>'],
  ['#dfc8a0','#513827','КОФЕ','ДОРОЖНЫЙ ТЕРМОС','СОХРАНЯЕТ ТЕПЛО','350 мл','<path d="M101 109h45v27a18 18 0 01-18 18h-9a18 18 0 01-18-18zm46 1h8a13 13 0 010 26h-8v-7h7a6 6 0 000-12h-7zM109 91h4v13h-4zm14-5h4v18h-4zm14 5h4v13h-4z"/>'],
  ['#e5dfcc','#9c302b','АПТЕЧКА','ПЕРВАЯ ПОМОЩЬ','ИНДИВИДУАЛЬНЫЙ НАБОР','01 комплект','<path d="M118 88h20v19h19v20h-19v19h-20v-19H99v-20h19z"/>'],
  ['#ead6b4','#ab522c','ГРЕЛКА','ТЕПЛОВОЙ ПАКЕТ','ОДНОРАЗОВАЯ • СУХОЕ ТЕПЛО','01 пакет','<path d="M110 143c-22-22 13-27 0-52m19 52c-22-22 13-27 0-52m19 52c-22-22 13-27 0-52" fill="none" stroke="currentColor" stroke-width="7"/>']
];
const english = [
 ['RATION','FIELD RATION','READY TO EAT','450 g'],
 ['WATER','DRINKING WATER','STILL WATER','500 ml'],
 ['COFFEE','TRAVEL FLASK','KEEPS DRINKS WARM','350 ml'],
 ['FIRST AID','MEDICAL KIT','PERSONAL SUPPLIES','01 kit'],
 ['HEAT PACK','POCKET WARMER','SINGLE USE / DRY HEAT','01 pack']
];
async function generate(russian) {
let svg = '<svg xmlns="http://www.w3.org/2000/svg" width="2048" height="512"><rect width="2048" height="512" fill="#272c2a"/>';
labels.forEach(([paper,ink,title,sub,note,amount,symbol],i) => {
  if (!russian) [title,sub,note,amount] = english[i];
  svg += `<g transform="translate(${i*256} 0)" fill="${ink}" color="${ink}" font-family="Arial, sans-serif" text-anchor="middle"><rect width="256" height="256" fill="${paper}"/><rect x="12" y="12" width="232" height="232" fill="none" stroke="${ink}" stroke-width="3"/><text x="128" y="32" font-size="11" letter-spacing="3">${russian ? 'ДОРОЖНЫЙ ЗАПАС' : 'TRAVEL SUPPLIES'}</text><path d="M25 40h206" stroke="${ink}"/><text x="128" y="69" font-size="25" font-weight="bold">${title}</text>${symbol}<text x="128" y="173" font-size="12" font-weight="bold">${sub}</text><text x="128" y="192" font-size="9">${note}</text><text x="59" y="228" font-size="13" font-weight="bold">${amount}</text>`;
  for (let b=0;b<35;b++) if ((b*7+3)%5<3) svg += `<rect x="${140+b*2.4}" y="208" width="${b%3===0?2:1}" height="22"/>`;
  svg += '</g>';
});
palette.forEach((color,i)=>svg+=`<rect x="${i*128}" y="320" width="128" height="128" fill="${color}"/>`);
svg += '</svg>';
const out = path.join(__dirname,'../DVSurvival.Game/ItemAssets');
fs.mkdirSync(out,{recursive:true});
await sharp(Buffer.from(svg)).png().toFile(path.join(out,russian ? 'provisions.png' : 'provisions-en.png'));
}
Promise.all([generate(true),generate(false)]).then(()=>console.log('Generated RU/EN opaque provision atlases.'));
