# ============================================================================
# MEGA OBBY — animations.py
# 7 animacji na rigu z character.py, czysto przez keyframe_insert (stabilne API
# w Blenderze 3.6 i 4.x, łącznie z nowymi "slotted actions"):
#   Idle · Walk · Run · Jump · Fall · Victory · Dance
# Pętle mają zgodną pierwszą i ostatnią klatkę (gładkie powtórzenia).
# Klucze: rotacje kości (Euler XYZ) + Root (góra/dół, krok rytmu).
# Klatki: 30 fps — po imporcie do Robloxa Editor wyświetli je jako KeyframeSequence.
# ============================================================================

import bpy
import math

import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.bpyutil import log  # noqa: E402


FPS = 30


def _reset_pose(armature):
    for pose_bone in armature.pose.bones:
        pose_bone.rotation_euler = (0, 0, 0)
        pose_bone.location = (0, 0, 0)


def _key(armature, action_name, frame, bone_rotations, root_location=None):
    """Ustawia pozę w danej klatce i wstawia klucze (rotacje + opcjonalny Root)."""
    scene_frame = frame + 1  # Blender liczy klatki od 1
    bpy.context.scene.frame_set(scene_frame)
    for bone_name, rotation in bone_rotations.items():
        bone = armature.pose.bones.get(bone_name)
        if bone is None:
            continue
        bone.rotation_euler = rotation
        armature.keyframe_insert(
            data_path=f'pose.bones["{bone_name}"].rotation_euler', frame=scene_frame
        )
    if root_location is not None:
        root = armature.pose.bones.get("Root")
        if root is not None:
            root.location = root_location
            armature.keyframe_insert(
                data_path='pose.bones["Root"].location', frame=scene_frame
            )


def _new_action(armature, name, loop=True):
    action = bpy.data.actions.new("MegaObby_" + name)
    if armature.animation_data is None:
        armature.animation_data_create()
    armature.animation_data.action = action
    # pętla: last+first (Blender 4.4+ ma sloty; extrapolacja działa przez NLA,
    # więc dla bezpieczeństwa i tak klonuję pierwszą klatkę na koniec)
    return action


def _deg(rad_triple):
    return tuple(math.radians(angle) for angle in rad_triple)


def make_idle(armature):
    _new_action(armature, "Idle")
    keys = [
        (0, {"Torso": _deg((0, 0, 0)), "Head": _deg((0, 0, 0)),
             "ArmL": _deg((0, 0, 3)), "ArmR": _deg((0, 0, -3))}, (0, 0, 0)),
        (FPS // 2, {"Torso": _deg((1.5, 0, 0)), "Head": _deg((-2, 0, 0)),
                    "ArmL": _deg((0, 0, 5)), "ArmR": _deg((0, 0, -5))}, (0, 0, 0.03)),
        (FPS, {"Torso": _deg((0, 0, 0)), "Head": _deg((0, 0, 0)),
               "ArmL": _deg((0, 0, 3)), "ArmR": _deg((0, 0, -3))}, (0, 0, 0)),
    ]
    for frame, rotations, root in keys:
        _key(armature, "Idle", frame, rotations, root)


def make_walk(armature):
    _new_action(armature, "Walk")
    swing = 35
    keys = [
        (0, {"ArmL": _deg((swing, 0, 3)), "ArmR": _deg((-swing, 0, -3)),
             "LegL": _deg((-swing, 0, 0)), "LegR": _deg((swing, 0, 0)),
             "Torso": _deg((0, 2, 0))}, (0, 0, 0)),
        (FPS // 4, {"ArmL": _deg((0, 0, 3)), "ArmR": _deg((0, 0, -3)),
                    "LegL": _deg((0, 0, 0)), "LegR": _deg((0, 0, 0)),
                    "Torso": _deg((2, 0, 0))}, (0, 0, 0.05)),
        (FPS // 2, {"ArmL": _deg((-swing, 0, 3)), "ArmR": _deg((swing, 0, -3)),
                    "LegL": _deg((swing, 0, 0)), "LegR": _deg((-swing, 0, 0)),
                    "Torso": _deg((0, -2, 0))}, (0, 0, 0)),
        (3 * FPS // 4, {"ArmL": _deg((0, 0, 3)), "ArmR": _deg((0, 0, -3)),
                        "LegL": _deg((0, 0, 0)), "LegR": _deg((0, 0, 0)),
                        "Torso": _deg((2, 0, 0))}, (0, 0, 0.05)),
        (FPS, {"ArmL": _deg((swing, 0, 3)), "ArmR": _deg((-swing, 0, -3)),
               "LegL": _deg((-swing, 0, 0)), "LegR": _deg((swing, 0, 0)),
               "Torso": _deg((0, 2, 0))}, (0, 0, 0)),
    ]
    for frame, rotations, root in keys:
        _key(armature, "Walk", frame, rotations, root)


def make_run(armature):
    _new_action(armature, "Run")
    swing = 60
    lean = 12
    keys = [
        (0, {"ArmL": _deg((swing, 0, 6)), "ArmR": _deg((-swing, 0, -6)),
             "LegL": _deg((-swing, 0, 0)), "LegR": _deg((swing, 0, 0)),
             "Torso": _deg((lean, 3, 0))}, (0, 0, 0)),
        (FPS // 4, {"ArmL": _deg((-swing // 3, 0, 6)), "ArmR": _deg((swing // 3, 0, -6)),
                    "LegL": _deg((swing // 3, 0, 0)), "LegR": _deg((-swing // 3, 0, 0)),
                    "Torso": _deg((lean + 3, 0, 0))}, (0, 0, 0.08)),
        (FPS // 2, {"ArmL": _deg((-swing, 0, 6)), "ArmR": _deg((swing, 0, -6)),
                    "LegL": _deg((swing, 0, 0)), "LegR": _deg((-swing, 0, 0)),
                    "Torso": _deg((lean, -3, 0))}, (0, 0, 0)),
        (3 * FPS // 4, {"ArmL": _deg((swing // 3, 0, 6)), "ArmR": _deg((-swing // 3, 0, -6)),
                        "LegL": _deg((-swing // 3, 0, 0)), "LegR": _deg((swing // 3, 0, 0)),
                        "Torso": _deg((lean + 3, 0, 0))}, (0, 0, 0.08)),
        (FPS, {"ArmL": _deg((swing, 0, 6)), "ArmR": _deg((-swing, 0, -6)),
               "LegL": _deg((-swing, 0, 0)), "LegR": _deg((swing, 0, 0)),
               "Torso": _deg((lean, 3, 0))}, (0, 0, 0)),
    ]
    for frame, rotations, root in keys:
        _key(armature, "Run", frame, rotations, root)


def make_jump(armature):
    _new_action(armature, "Jump")
    keys = [
        (0, {"Torso": _deg((0, 0, 0)), "ArmL": _deg((0, 0, 3)), "ArmR": _deg((0, 0, -3)),
             "LegL": _deg((0, 0, 0)), "LegR": _deg((0, 0, 0))}, (0, 0, -0.15)),
        (FPS // 6, {"Torso": _deg((14, 0, 0)), "ArmL": _deg((40, 0, 10)), "ArmR": _deg((40, 0, -10)),
                    "LegL": _deg((35, 0, 0)), "LegR": _deg((35, 0, 0))}, (0, 0, -0.35)),  # przyklęk
        (FPS // 3, {"Torso": _deg((-10, 0, 0)), "ArmL": _deg((-150, 0, 8)), "ArmR": _deg((-150, 0, -8)),
                    "LegL": _deg((-8, 0, 0)), "LegR": _deg((-8, 0, 0))}, (0, 0, 0.15)),  # wybicie
        (FPS // 2, {"Torso": _deg((-6, 0, 0)), "ArmL": _deg((-170, 0, 5)), "ArmR": _deg((-170, 0, -5)),
                    "LegL": _deg((10, 0, 0)), "LegR": _deg((-10, 0, 0))}, (0, 0, 0.25)),  # szczyt
        (FPS, {"Torso": _deg((0, 0, 0)), "ArmL": _deg((0, 0, 3)), "ArmR": _deg((0, 0, -3)),
               "LegL": _deg((0, 0, 0)), "LegR": _deg((0, 0, 0))}, (0, 0, 0)),
    ]
    for frame, rotations, root in keys:
        _key(armature, "Jump", frame, rotations, root)


def make_fall(armature):
    _new_action(armature, "Fall")
    keys = [
        (0, {"Torso": _deg((6, 0, 0)), "ArmL": _deg((-120, 0, 25)), "ArmR": _deg((-120, 0, -25)),
             "LegL": _deg((18, 0, 0)), "LegR": _deg((-6, 0, 0))}, (0, 0, 0.1)),
        (FPS // 2, {"Torso": _deg((4, 0, 0)), "ArmL": _deg((-135, 0, 35)), "ArmR": _deg((-135, 0, -35)),
                    "LegL": _deg((26, 0, 0)), "LegR": _deg((-12, 0, 0))}, (0, 0, 0.16)),
        (FPS, {"Torso": _deg((6, 0, 0)), "ArmL": _deg((-120, 0, 25)), "ArmR": _deg((-120, 0, -25)),
               "LegL": _deg((18, 0, 0)), "LegR": _deg((-6, 0, 0))}, (0, 0, 0.1)),
    ]
    for frame, rotations, root in keys:
        _key(armature, "Fall", frame, rotations, root)


def make_victory(armature):
    _new_action(armature, "Victory")
    keys = [
        (0, {"ArmL": _deg((10, 0, 12)), "ArmR": _deg((10, 0, -12)),
             "Torso": _deg((0, 0, 0))}, (0, 0, 0)),
        (FPS // 4, {"ArmL": _deg((-160, 0, 18)), "ArmR": _deg((-160, 0, -18)),
                    "Torso": _deg((-5, 0, 0))}, (0, 0, 0.12)),
        (FPS // 2, {"ArmL": _deg((-140, 0, 30)), "ArmR": _deg((-140, 0, -30)),
                    "Torso": _deg((-3, 0, 0))}, (0, 0, 0.02)),
        (3 * FPS // 4, {"ArmL": _deg((-165, 0, 15)), "ArmR": _deg((-165, 0, -15)),
                        "Torso": _deg((-6, 0, 0))}, (0, 0, 0.12)),
        (FPS, {"ArmL": _deg((10, 0, 12)), "ArmR": _deg((10, 0, -12)),
               "Torso": _deg((0, 0, 0))}, (0, 0, 0)),
    ]
    for frame, rotations, root in keys:
        _key(armature, "Victory", frame, rotations, root)


def make_dance(armature):
    _new_action(armature, "Dance")
    keys = [
        (0, {"ArmL": _deg((-90, 0, 40)), "ArmR": _deg((-20, 0, -50)),
             "LegL": _deg((10, 0, 0)), "LegR": _deg((-10, 0, 0)),
             "Torso": _deg((0, 8, 0))}, (0, 0, -0.05)),
        (FPS // 4, {"ArmL": _deg((-40, 0, 55)), "ArmR": _deg((-110, 0, -35)),
                    "LegL": _deg((-6, 0, 0)), "LegR": _deg((12, 0, 0)),
                    "Torso": _deg((3, -8, 0))}, (0, 0, 0.05)),
        (FPS // 2, {"ArmL": _deg((-20, 0, 50)), "ArmR": _deg((-90, 0, -40)),
                    "LegL": _deg((10, 0, 0)), "LegR": _deg((-10, 0, 0)),
                    "Torso": _deg((0, 8, 0))}, (0, 0, -0.05)),
        (3 * FPS // 4, {"ArmL": _deg((-110, 0, 35)), "ArmR": _deg((-40, 0, -55)),
                        "LegL": _deg((-6, 0, 0)), "LegR": _deg((12, 0, 0)),
                        "Torso": _deg((3, -8, 0))}, (0, 0, 0.05)),
        (FPS, {"ArmL": _deg((-90, 0, 40)), "ArmR": _deg((-20, 0, -50)),
               "LegL": _deg((10, 0, 0)), "LegR": _deg((-10, 0, 0)),
               "Torso": _deg((0, 8, 0))}, (0, 0, -0.05)),
    ]
    for frame, rotations, root in keys:
        _key(armature, "Dance", frame, rotations, root)


def build_all_animations(armature):
    builders = [make_idle, make_walk, make_run, make_jump, make_fall, make_victory, make_dance]
    for builder in builders:
        _reset_pose(armature)
        builder(armature)
    _reset_pose(armature)
    log(f"Animacje zbudowane: {len(builders)} (Idle, Walk, Run, Jump, Fall, Victory, Dance).")
    return [action for action in bpy.data.actions if action.name.startswith("MegaObby_")]


if __name__ == "__main__":
    armature = bpy.data.objects.get("MegaObbyRig")
    if armature is None:
        raise RuntimeError("Najpierw uruchom character.py (brak MegaObbyRig w scenie).")
    build_all_animations(armature)
