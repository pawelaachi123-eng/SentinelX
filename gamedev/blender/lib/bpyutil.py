# ============================================================================
# MEGA OBBY — bpyutil (wspólne narzędzia Blendera dla generatorów)
# Kolorystyka 1:1 z src/Shared/GameConfig.lua (wyspy, akcenty), skala:
# 1 stud Robloxa = 0.28 m — wszystkie budynki/propy liczymy w studach.
# Działa w Blenderze 3.6 LTS oraz 4.x (bez zależności od usuniętych API).
# Uruchamianie: Blender → zakładka Scripting → Open → build_all.py → Run.
# ============================================================================

import bpy
import math

STUDS = 0.28  # 1 stud Robloxa w metrach

# Kolory z GameConfig.Islands + akcenty UI
COLORS = {
    "meadow": (106 / 255, 190 / 255, 82 / 255, 1.0),
    "desert": (222 / 255, 184 / 255, 105 / 255, 1.0),
    "ice": (168 / 255, 220 / 255, 240 / 255, 1.0),
    "lava": (220 / 255, 90 / 255, 60 / 255, 1.0),
    "sky": (150 / 255, 190 / 255, 255 / 255, 1.0),
    "cyber": (120 / 255, 240 / 255, 230 / 255, 1.0),
    "accent": (34 / 255, 211 / 255, 238 / 255, 1.0),
    "gold": (255 / 255, 200 / 255, 40 / 255, 1.0),
    "danger": (255 / 255, 60 / 255, 60 / 255, 1.0),
    "wood": (110 / 255, 74 / 255, 48 / 255, 1.0),
    "leaf": (70 / 255, 160 / 255, 80 / 255, 1.0),
    "stone": (130 / 255, 132 / 255, 140 / 255, 1.0),
    "skin": (255 / 255, 204 / 255, 153 / 255, 1.0),
    "shirt": (40 / 255, 90 / 255, 200 / 255, 1.0),
    "pants": (50 / 255, 55 / 255, 70 / 255, 1.0),
}


def clean_scene():
    """Usuwa wszystko — generator startuje z czystym plikiem."""
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block_name in (bpy.data.meshes, bpy.data.materials, bpy.data.armatures, bpy.data.actions):
        for block in list(block_name):
            if block.users == 0:
                block_name.remove(block)


def ensure_collection(name):
    """Kolekcja o podanej nazwie (tworzy, jeśli nie ma) — podłączona do sceny."""
    collection = bpy.data.collections.get(name)
    if collection is None:
        collection = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(collection)
    return collection


def material(name, color, roughness=0.6, metallic=0.0, emission=0.0):
    """Prosty materiał Principled; emission > 0 daje neon (jak Material.Neon)."""
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is None:
        return mat
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Roughness"].default_value = roughness
    if "Metallic" in bsdf.inputs:
        bsdf.inputs["Metallic"].default_value = metallic
    if emission > 0:
        # Blender 4.x: "Emission Color" + "Emission Strength"; 3.6: "Emission"
        if "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = color
            bsdf.inputs["Emission Strength"].default_value = emission
        elif "Emission" in bsdf.inputs:
            bsdf.inputs["Emission"].default_value = color
            if "Emission Strength" in bsdf.inputs:
                bsdf.inputs["Emission Strength"].default_value = emission
    return mat


def add_box(collection, name, size_studs, location_studs, mat, rotation=(0, 0, 0)):
    """Prostopadłościan w studach (automatycznie ×STUDS do metrów)."""
    size = [s * STUDS for s in size_studs]
    location = [c * STUDS for c in location_studs]
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = (size[0], size[1], size[2])
    bpy.ops.object.transform_apply(scale=True)
    obj.data.materials.append(mat)
    for coll in list(obj.users_collection):
        coll.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def add_sphere(collection, name, radius_studs, location_studs, mat, subdivisions=2):
    bpy.ops.mesh.primitive_ico_sphere_add(
        subdivisions=subdivisions, radius=radius_studs * STUDS,
        location=[c * STUDS for c in location_studs],
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(mat)
    for coll in list(obj.users_collection):
        coll.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def add_cylinder(collection, name, radius_studs, depth_studs, location_studs, mat, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(
        radius=radius_studs * STUDS, depth=depth_studs * STUDS,
        location=[c * STUDS for c in location_studs], rotation=rotation,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(mat)
    for coll in list(obj.users_collection):
        coll.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def add_torus(collection, name, major_studs, minor_studs, location_studs, mat, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_studs * STUDS, minor_radius=minor_studs * STUDS,
        location=[c * STUDS for c in location_studs], rotation=rotation,
    )
    obj = bpy.context.active_object
    obj.name = name
    obj.data.materials.append(mat)
    for coll in list(obj.users_collection):
        coll.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def triangulate_and_shade(obj):
    """Triangulacja (Roblox i tak trianguluje) + smooth z autosmooth."""
    modifier = obj.modifiers.new("Triangulate", "TRIANGULATE")
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def apply_all(obj):
    bpy.context.view_layer.objects.active = obj
    for modifier in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def select_only(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        if obj is not None:
            obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def export_fbx(path, objects, scale=1.0):
    """Eksport FBX tylko dla wskazanych obiektów. Blender 4.5+ ma nowy operator
    (bpy.ops.export.fbx) — próbujemy oba, żeby działało na LTS i nowszych."""
    select_only(objects)
    common = {
        "filepath": path,
        "use_selection": True,
        "apply_scale_options": "FBX_SCALE_ALL",
        "bake_space_transform": True,
        "object_types": {"MESH", "ARMATURE"},
        "add_leaf_bones": False,
    }
    try:
        bpy.ops.export_scene.fbx(**common, global_scale=scale)
    except AttributeError:
        bpy.ops.export.fbx(**common, global_scale=scale)
    print(f"[MegaObby] FBX zapisany: {path}")


def export_glb(path, objects):
    select_only(objects)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True)
    print(f"[MegaObby] GLB zapisany: {path}")


def studs_to_meters(value_studs):
    return value_studs * STUDS


def log(message):
    print("[MegaObby] " + str(message))
