"""Read-only geometry audit of native heater meshes, including welded cabin details."""
import sys
import json
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

env = UnityPy.load(sys.argv[1])
objects = {o.path_id: o for o in env.objects}
for mesh_id in [5108, 4398, 5301, 3532]:
    mesh = objects[mesh_id].read()
    handler = MeshHandler(mesh)
    handler.process()
    vertices = handler.m_Vertices
    tris = [t for sub in handler.get_triangles() for t in sub]
    if mesh_id in (4398, 5301):
        lo, hi = ((1.1599, -.2948, 2.0695), (1.1719, -.2792, 2.1038)) if mesh_id == 4398 else ((-.6680, 2.2016, -3.6073), (-.6264, 2.2436, -3.5912))
        selected = {i for i,v in enumerate(vertices) if all(lo[d] <= v[d] <= hi[d] for d in range(3))}
        taken = [t for t in tris if all(i in selected for i in t)]
        print('RUNTIME_SPLIT', mesh_id, 'selected vertices',len(selected),'triangles',len(taken), 'of',len(tris))
        assert 12 <= len(taken) <= 160
    parent = list(range(len(vertices)))
    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    def union(a, b):
        parent[find(a)] = find(b)
    # Weld UV/normal seam duplicates only for connectivity analysis.
    positions = {}
    for i, v in enumerate(vertices):
        key = tuple(round(x, 5) for x in v)
        if key in positions:
            union(i, positions[key])
        positions[key] = i
    for a,b,c in tris:
        union(a,b)
        union(b,c)
    groups = {}
    for i in range(len(vertices)):
        groups.setdefault(find(i), []).append(i)
    print('MESH', mesh_id, mesh.m_Name, 'readable', mesh.m_IsReadable, 'vertices', len(vertices))
    rows = []
    for ids in groups.values():
        vs = [vertices[i] for i in ids]
        low = [min(v[d] for v in vs) for d in range(3)]
        high = [max(v[d] for v in vs) for d in range(3)]
        center = [(a+b)/2 for a,b in zip(low,high)]
        # DE2 mesh is authored Z-up, cabinet label is near (1.147,-.291,2.037).
        if mesh_id in (5108, 4398) and not (high[0] > 1.05 and low[0] < 1.25 and high[1] > -.36 and low[1] < -.23 and high[2] > 2.04 and low[2] < 2.22):
            continue
        if mesh_id == 5301 and not (high[0] > -.72 and low[0] < -.55 and high[1] > 2.20 and low[1] < 2.40 and high[2] > -3.65 and low[2] < -3.4):
            continue
        rows.append(dict(vertices=len(ids), low=low, high=high, center=center))
    for row in rows:
        print(json.dumps(row))
    if mesh_id == 5108:
        near = [v for v in vertices if 1.05 < v[0] < 1.25 and -.36 < v[1] < -.23 and 2.04 < v[2] < 2.22]
        print('NEAR', len(near), sorted(set(tuple(round(x, 5) for x in v) for v in near)))
        target = (1.147, -.291, 2.13)
        print('CLOSEST', sorted(set(vertices), key=lambda v: sum((v[d]-target[d])**2 for d in range(3)))[:8])
        print('BOUNDS', mesh.m_LocalAABB)
    if mesh_id == 5301:
        near = sorted(set(tuple(round(x,6) for x in v) for v in vertices if -.68<v[0]<-.62 and 2.195<v[1]<2.255 and -3.615<v[2]<-3.56))
        print('KNOB_VERTICES', near)
