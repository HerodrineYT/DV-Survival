"""Orthographic technical preview of the installed game's stock heater geometry."""
import sys
from pathlib import Path
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
from PIL import Image, ImageDraw

env = UnityPy.load(sys.argv[1])
objects = {o.path_id: o for o in env.objects}
out = Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)
for mesh_id, name, region, axes, sign in [
    (5301, 'dm3-front', (-1.2,1.2,1.6,2.6,-1.9,-1.5), (0,1,2), -1),
    (5301, 'dm3-back', (-1.2,1.2,1.6,2.9,-3.8,-3.3), (0,1,2), 1),
    (4398, 'de2-heater', (1.10,1.19,2.05,2.12,-.32,-.26), (0,2,1), 1),
]:
    h = MeshHandler(objects[mesh_id].read()); h.process()
    verts = h.m_Vertices
    tris = [t for sub in h.get_triangles() for t in sub]
    x,y,z=axes
    xmin,xmax,ymin,ymax,zmin,zmax=region
    scale=1000/(xmax-xmin)
    im=Image.new('RGB',(1040,int((ymax-ymin)*scale)+40),(25,34,43))
    draw=ImageDraw.Draw(im)
    visible=[]
    for tri in tris:
        vs=[verts[i] for i in tri]
        center=[sum(v[d] for v in vs)/3 for d in range(3)]
        if not (xmin<center[x]<xmax and ymin<center[y]<ymax and zmin<center[z]<zmax): continue
        visible.append((sign*center[z],vs))
    for depth,vs in sorted(visible):
        shade=int(90+110*(sign*depth-zmin)/(zmax-zmin))
        shade=max(45,min(230,shade))
        pts=[(20+(v[x]-xmin)*scale,20+(ymax-v[y])*scale) for v in vs]
        draw.polygon(pts,fill=(shade,shade,shade),outline=(70,90,100))
    for coord in [i/10 for i in range(-12,13)]:
        if xmin<coord<xmax: draw.text((20+(coord-xmin)*scale,5),str(coord),fill='yellow')
    im.save(out/(name+'.png'))
    print(out/(name+'.png'))
