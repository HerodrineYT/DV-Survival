import json
import sys

import UnityPy


ROOTS = {
    "LocoDE2_Interior",
    "LocoDH4_Interior",
    "LocoDE6_Interior",
    "LocoDE2Exploded_Interior",
    "LocoDH4Exploded_Interior",
    "LocoDE6Exploded_Interior",
    "LocoDM3_Interior",
    "LocoDM1U_Interior",
}
NAME_TERMS = ("heat", "warm", "heater", "radiator", "fan", "vent", "temperature")
COMPONENT_TERMS = ("control", "interact", "switch", "lever", "knob", "rotary", "button")


def pointer_path_id(value):
    pointer = getattr(value, "component", value)
    return getattr(pointer, "path_id", 0)


def main():
    if len(sys.argv) < 2:
        print("usage: inspect_loco_heaters.py <resources.assets> [path-filter ...]", file=sys.stderr)
        return 64

    path_filters = tuple(value.casefold() for value in sys.argv[2:])

    environment = UnityPy.load(sys.argv[1])
    objects = {obj.path_id: obj for obj in environment.objects}
    game_objects = {}
    transforms = {}
    components = {}

    for obj in environment.objects:
        if obj.type.name == "GameObject":
            try:
                game_objects[obj.path_id] = obj.read()
            except Exception:
                pass
        elif obj.type.name == "Transform":
            try:
                transforms[obj.path_id] = obj.read()
            except Exception:
                pass

    transform_by_game_object = {}
    for transform_id, transform in transforms.items():
        game_object_id = getattr(transform.m_GameObject, "path_id", 0)
        transform_by_game_object[game_object_id] = transform_id

    def hierarchy(game_object_id):
        names = []
        seen = set()
        while game_object_id and game_object_id not in seen:
            seen.add(game_object_id)
            game_object = game_objects.get(game_object_id)
            if game_object is None:
                break
            names.append(getattr(game_object, "m_Name", "") or "")
            transform = transforms.get(transform_by_game_object.get(game_object_id, 0))
            if transform is None:
                break
            parent_transform = transforms.get(getattr(transform.m_Father, "path_id", 0))
            game_object_id = (getattr(parent_transform.m_GameObject, "path_id", 0)
                              if parent_transform is not None else 0)
        return list(reversed(names))

    rows = []
    for game_object_id, game_object in game_objects.items():
        path_parts = hierarchy(game_object_id)
        root = next((part for part in path_parts if part in ROOTS), None)
        if root is None:
            continue
        component_rows = []
        component_names = []
        for component in getattr(game_object, "m_Component", []):
            path_id = pointer_path_id(component)
            component_obj = objects.get(path_id)
            if component_obj is None:
                continue
            class_name = component_obj.type.name
            asset_name = ""
            if class_name == "MeshFilter":
                try:
                    mesh_filter = component_obj.read()
                    mesh = mesh_filter.m_Mesh.read()
                    asset_name = getattr(mesh, "m_Name", "") or ""
                except Exception:
                    pass
            if class_name == "MonoBehaviour":
                try:
                    data = component_obj.read(check_read=False)
                    script = data.m_Script.read()
                    class_name = getattr(script, "m_ClassName", "") or class_name
                except Exception:
                    pass
            component_names.append(class_name + (" " + asset_name if asset_name else ""))
            component_row = {"path_id": path_id, "class": class_name,
                             "asset_name": asset_name}
            if path_filters:
                try:
                    component_row["data"] = component_obj.read_typetree()
                except Exception:
                    pass
            component_rows.append(component_row)
        haystack = (getattr(game_object, "m_Name", "") + " " + " ".join(component_names)).casefold()
        name_match = any(term in haystack for term in NAME_TERMS)
        control_match = any(term in " ".join(component_names).casefold()
                            for term in COMPONENT_TERMS)
        if name_match or control_match or path_filters:
            transform = transforms.get(transform_by_game_object.get(game_object_id, 0))
            local_position = None
            local_rotation = None
            local_scale = None
            if transform is not None:
                position = getattr(transform, "m_LocalPosition", None)
                if position is not None:
                    local_position = [position.x, position.y, position.z]
                rotation = getattr(transform, "m_LocalRotation", None)
                if rotation is not None:
                    local_rotation = [rotation.x, rotation.y, rotation.z, rotation.w]
                scale = getattr(transform, "m_LocalScale", None)
                if scale is not None:
                    local_scale = [scale.x, scale.y, scale.z]
            path = "/".join(path_parts)
            if path_filters and not any(value in path.casefold() for value in path_filters):
                continue
            rows.append({
                "root": root,
                "path": path,
                "active": bool(getattr(game_object, "m_IsActive", True)),
                "local_position": local_position,
                "local_rotation": local_rotation,
                "local_scale": local_scale,
                "components": component_rows,
            })

    print(json.dumps(rows, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
