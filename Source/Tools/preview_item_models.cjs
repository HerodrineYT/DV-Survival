// Offline Z-buffer preview of production geometry, not a Unity/FPS test.
const fs=require('fs'),path=require('path'),sharp=require('sharp');
const dir=path.resolve(__dirname,'../artifacts/previews/items-0.6.1');
const names=['Meal','Water','Coffee','FirstAid','HeatPack'],titles=['Паёк','Вода','Кофе · термос','Аптечка','Грелка'];
const dot=(a,b)=>a.reduce((s,v,i)=>s+v*b[i],0),sub=(a,b)=>a.map((x,i)=>x-b[i]);
const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const edge=(a,b,x,y)=>(b[0]-a[0])*(y-a[1])-(b[1]-a[1])*(x-a[0]);
const right=[.94,0,.342],up=[-.1,.956,.275],view=[.327,.293,-.899];
async function main() {
  const {data:atlas,info}=await sharp(path.resolve(__dirname,'../DVSurvival.Game/ItemAssets/provisions.png')).ensureAlpha().raw().toBuffer({resolveWithObject:true});
  const W=3200,H=1200,buffer=Buffer.alloc(W*H*3),depth=new Float32Array(W*H).fill(-Infinity);
  for(let i=0;i<W*H;i++){buffer[i*3]=28;buffer[i*3+1]=36;buffer[i*3+2]=38;}
  let svg='<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="600"><text x="45" y="53" font-family="Arial" font-size="25" fill="#eadcc3">DVSurvival 0.6.1 — дорожные припасы</text><text x="45" y="82" font-family="Arial" font-size="15" fill="#9caaa9">Предпросмотр геометрии и этикеток · не скриншот из игры</text>';
  for(let k=0;k<names.length;k++) {
    const m=JSON.parse(fs.readFileSync(path.join(dir,names[k]+'.json'))),ox=165+k*315,oy=450,s=1120;
    const project=p=>[2*(ox+s*dot(p,right)),2*(oy-s*dot(p,up)),dot(p,view)];
    for(let i=0;i<m.triangles.length;i+=3) {
      const indices=m.triangles.slice(i,i+3),v=indices.map(j=>m.vertices[j]),p=v.map(project),uv=indices.map(j=>m.uv[j]);
      const normal=cross(sub(v[1],v[0]),sub(v[2],v[0]));
      if(dot(normal,view)<=1e-10)continue;
      const n=Math.sqrt(dot(normal,normal)),light=.64+.36*Math.max(0,dot(normal,[-.35,.75,-.56])/n);
      const area=edge(p[0],p[1],p[2][0],p[2][1]);
      if(area<=0)continue;
      const minX=Math.max(0,Math.floor(Math.min(...p.map(p=>p[0])))),maxX=Math.min(W-1,Math.ceil(Math.max(...p.map(p=>p[0]))));
      const minY=Math.max(0,Math.floor(Math.min(...p.map(p=>p[1])))),maxY=Math.min(H-1,Math.ceil(Math.max(...p.map(p=>p[1]))));
      for(let y=minY;y<=maxY;y++)for(let x=minX;x<=maxX;x++) {
        const a=edge(p[1],p[2],x+.5,y+.5)/area,b=edge(p[2],p[0],x+.5,y+.5)/area,c=1-a-b;
        if(a<0||b<0||c<0)continue;
        const z=a*p[0][2]+b*p[1][2]+c*p[2][2],pixel=y*W+x;
        if(z<depth[pixel])continue;
        depth[pixel]=z;
        const u=a*uv[0][0]+b*uv[1][0]+c*uv[2][0],vt=a*uv[0][1]+b*uv[1][1]+c*uv[2][1];
        const tx=Math.min(info.width-1,Math.max(0,Math.round(u*info.width-.5))),ty=Math.min(info.height-1,Math.max(0,Math.round((1-vt)*info.height-.5)));
        const t=(ty*info.width+tx)*4;
        for(let ch=0;ch<3;ch++)buffer[pixel*3+ch]=Math.round(atlas[t+ch]*light);
      }
    }
    svg+='<text x="'+ox+'" y="511" text-anchor="middle" font-family="Arial" font-size="20" fill="#eadcc3">'+titles[k]+'</text><text x="'+ox+'" y="538" text-anchor="middle" font-family="Arial" font-size="14" fill="#9caaa9">'+m.triangles.length/3+' треугольников · 1 меш</text>';
  }
  svg+='</svg>';
  const rendered=await sharp(buffer,{raw:{width:W,height:H,channels:3}}).resize(1600,600).png().toBuffer();
  await sharp(rendered).composite([{input:Buffer.from(svg)}]).png().toFile(path.join(dir,'models.png'));
  console.log(path.join(dir,'models.png'));
}
main().catch(e=>{console.error(e);process.exitCode=1;});
