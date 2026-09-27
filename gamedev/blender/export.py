# ============================================================================
# MEGA OBBY — export.py
# Eksport postaci (z rigiem i animacjami) oraz wszystkich propów do FBX
# (+ opcjonalnie GLB). Katalog: gamedev/blender/exports/ (obok skryptów).
#
# SKALA: modele budujemy w studach, biblioteka przelicza na metry (×0.28).
# Roblox Studio: Avatar Setup / Bulk Import → po imporcie sprawdź rozmiar
# MeshParta i w razie czego przeskaluj (Size) — szczegóły w README-BLENDER.md.
# ============================================================================

import bpy

import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.bpyutil import export_fbx, export_glb, log  # noqa: E402


EXPORT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "exports")


def _objects_of(collection):
    return [obj for obj in collection.objects if obj.type in ("MESH", "ARMATURE")]


def export_character(with_animations=True):
    collection = bpy.data.collections.get("Character")
    if collection is None:
        raise RuntimeError("Brak kolekcji Character — uruchom character.py.")
    os.makedirs(EXPORT_DIR, exist_ok=True)
    objects = _objects_of(collection)
    export_fbx(os.path.join(EXPORT_DIR, "character_rig.fbx"), objects)
    export_glb(os.path.join(EXPORT_DIR, "character_rig.glb"), objects)
    if with_animations:
        # animacje żyją na akcjach armatury — FBX zabiera akcje powiązane z rigiem
        actions = [action for action in bpy.data.actions if action.name.startswith("MegaObby_")]
        log(f"Eksport z animacjami: {len(actions)} akcji na rigu.")


def export_props():
    os.makedirs(EXPORT_DIR, exist_ok=True)
    count = 0
    for collection in bpy.data.collections:
        if not collection.name.startswith("Prop_"):
            continue
        objects = _objects_of(collection)
        if not objects:
            continue
        name = collection.name.replace("Prop_", "").lower()
        export_fbx(os.path.join(EXPORT_DIR, f"prop_{name}.fbx"), objects)
        count += 1
    log(f"Propy wyeksportowane: {count} plików FBX w {EXPORT_DIR}")


def export_all(with_animations=True):
    export_character(with_animations)
    export_props()
    log("EKSPORT ZAKOŃCZONY — pliki w gamedev/blender/exports/")


if __name__ == "__main__":
    export_all()
