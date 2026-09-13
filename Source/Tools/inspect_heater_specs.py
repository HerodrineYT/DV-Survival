import sys, json
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.MeshHelper import MeshHandler

env = UnityPy.load(sys.argv[1])
generator = TypeTreeGenerator('2019.4.40f1')
generator.load_local_dll_folder(sys.argv[2])
env.typetree_generator = generator
objects = {o.path_id:o for o in env.objects}
for obj_id in [301169]:
    print('SPEC', obj_id, json.dumps(objects[obj_id].read_typetree(),indent=2))
for mesh_id in [3532]:
    handler=MeshHandler(objects[mesh_id].read()); handler.process()
    print('VERTICES',mesh_id, sorted(set(tuple(round(x,6) for x in v) for v in handler.m_Vertices)))
