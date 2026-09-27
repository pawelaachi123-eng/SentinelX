# ============================================================================
# MEGA OBBY — character.py
# Generator postaci w stylu R6 (klocki jak Roblox): tors, głowa, 2 ręce,
# 2 nogi + ARMATURA (Root → Torso → Head / ArmL / ArmR / LegL / LegR) i grupy
# wierzchołków przypisane 1:1 do kości. Po eksporcie FBX Roblox rozpozna rig
# i animacje z animations.py (Importer 3D / Bulk Import).
# Wymiary w STUDACH (R6): tors 2×2×1, głowa 1.2, ręce/nogi 1×2×1.
# ============================================================================

import bpy
import math
from mathutils import Vector

import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.bpyutil import (  # noqa: E402
    STUDS, COLORS, ensure_collection, material, add_box,
)


BONE_HIERARCHY = {
    "Root": None,
    "Torso": "Root",
    "Head": "Torso",
    "ArmL": "Torso",
    "ArmR": "Torso",
    "LegL": "Torso",
    "LegR": "Torso",
}

# Pozycje główek kości (studs, Z w górę — Blender ma Z w górę domyślnie)
BONE_HEADS = {
    "Root": (0, 0, 3),
    "Torso": (0, 0, 3),
    "Head": (0, 0, 4.6),
    "ArmL": (-1.5, 0, 4),
    "ArmR": (1.5, 0, 4),
    "LegL": (-0.5, 0, 1),
    "LegR": (0.5, 0, 1),
}

# Geometria części (studs): [nazwa, rozmiar, pozycja środka, kość, materiał]
PARTS = [
    ("Torso", (2, 1, 2), (0, 0, 3), "Torso", "shirt"),
    ("Head", (1.2, 1.2, 1.2), (0, 0, 4.6), "Head", "skin"),
    ("ArmL", (1, 1, 2), (-1.5, 0, 3), "ArmL", "shirt"),
    ("ArmR", (1, 1, 2), (1.5, 0, 3), "ArmR", "shirt"),
    ("LegL", (1, 1, 2), (-0.5, 0, 1), "LegL", "pants"),
    ("LegR", (1, 1, 2), (0.5, 0, 1), "LegR", "pants"),
]


def build_armature(collection):
    armature_data = bpy.data.armatures.new("MegaObbyRig")
    armature = bpy.data.objects.new("MegaObbyRig", armature_data)
    collection.objects.link(armature)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="EDIT")

    edit_bones = {}
    for bone_name, parent_name in BONE_HIERARCHY.items():
        bone = armature_data.edit_bones.new(bone_name)
        head = Vector([c * STUDS for c in BONE_HEADS[bone_name]])
        tail = head.copy()
        if bone_name in ("ArmL", "ArmR", "LegL", "LegR"):
            tail.z -= 2 * STUDS  # kości kończyn idzą w dół
        else:
            tail.z += 1.2 * STUDS
        bone.head = head
        bone.tail = tail
        edit_bones[bone_name] = bone

    for bone_name, parent_name in BONE_HIERARCHY.items():
        if parent_name:
            edit_bones[bone_name].parent = edit_bones[parent_name]
            edit_bones[bone_name].use_connect = bone_name in ("Head", "ArmL", "ArmR", "LegL", "LegR")

    bpy.ops.object.mode_set(mode="OBJECT")
    return armature


def assign_vertex_groups(mesh_objects, armature):
    """Każda część idzie w całości do swojej kości — prosto i przewidywalnie."""
    for obj, bone_name in mesh_objects:
        group = obj.vertex_groups.new(name=bone_name)
        indices = [v.index for v in obj.data.vertices]
        group.add(indices, 1.0, "REPLACE")
        modifier = obj.modifiers.new("Armature", "ARMATURE")
        modifier.object = armature
        # skórka: rodzic po numerze (parent_type) — export FBX zachowa hierarchię
        obj.parent = armature
        obj.parent_type = "BONE"
        obj.parent_bone = bone_name


def build_character():
    collection = ensure_collection("Character")
    skin = material("skin", COLORS["skin"], roughness=0.55)
    shirt = material("shirt", COLORS["shirt"], roughness=0.6)
    pants = material("pants", COLORS["pants"], roughness=0.6)
    mats = {"skin": skin, "shirt": shirt, "pants": pants}

    armature = build_armature(collection)

    mesh_objects = []
    for name, size, location, bone, mat_key in PARTS:
        obj = add_box(collection, "Char_" + name, size, location, mats[mat_key])
        mesh_objects.append((obj, bone))

    assign_vertex_groups(mesh_objects, armature)

    log(f"Postać zbudowana: {len(mesh_objects)} części, 7 kości.")
    return armature, [obj for obj, _ in mesh_objects]


if __name__ == "__main__":
    clean_scene()
    build_character()
