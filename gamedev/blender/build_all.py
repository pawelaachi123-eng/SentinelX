# ============================================================================
# MEGA OBBY — build_all.py (JEDEN PRZYCISK)
# Odpal w Blenderze: zakładka Scripting → Open → build_all.py → Run Script.
# Efekt: czysta scena → postać R6 z rigiem → 7 animacji → 11 propów →
# eksport FBX+GLB do gamedev/blender/exports/.
# Wymaga: Blender 3.6 LTS lub 4.x (testowane pod API 4.x z fallbackami 3.6).
# ============================================================================

import bpy

import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.bpyutil import clean_scene, log  # noqa: E402
import character  # noqa: E402
import animations  # noqa: E402
import props  # noqa: E402
import export  # noqa: E402


def main():
    log("MEGA OBBY — generator modeli: start.")
    clean_scene()

    armature, character_meshes = character.build_character()
    animations.build_all_animations(armature)
    props.build_all_props()

    export.export_all(with_animations=True)
    log("GOTOWE. Import do Robloxa: Studio → Avatar Setup / Bulk Import.")
    log("README-BLENDER.md ma instrukcję krok po kroku (rig, animacje, skala).")


if __name__ == "__main__":
    main()
