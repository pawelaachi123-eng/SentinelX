# ============================================================================
# MEGA OBBY — props.py
# Propy świata w studach (1 stud = 0.28 m — spójne z grą):
#   Coin · Gem · CheckpointFlag · SpringPad · KillBrick · MovingPlatform ·
#   Tree · Rock · Cloud · PortalRing · Chest
# Każdy w osobnej kolekcji → eksport do osobnego FBX (export.py).
# Wyspowe warianty kolorów biorę z tej samej palety co GameConfig.lua.
# ============================================================================

import math

import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.bpyutil import (  # noqa: E402
    COLORS, ensure_collection, material, add_box, add_sphere,
    add_cylinder, add_torus, triangulate_and_shade, log,
)


def build_coin():
    collection = ensure_collection("Prop_Coin")
    gold = material("gold", COLORS["gold"], roughness=0.25, metallic=0.85, emission=0.35)
    coin = add_cylinder(collection, "Coin", 1.1, 0.25, (0, 0, 1.5), gold, rotation=(math.pi / 2, 0, 0))
    triangulate_and_shade(coin)
    log("Prop: Coin")
    return collection


def build_gem():
    collection = ensure_collection("Prop_Gem")
    gem = material("gem", COLORS["accent"], roughness=0.1, metallic=0.3, emission=0.6)
    obj = add_sphere(collection, "Gem", 0.9, (0, 0, 1), gem, subdivisions=1)  # low-poly brylant
    obj.scale = (1.0, 1.0, 1.45)
    triangulate_and_shade(obj)
    log("Prop: Gem")
    return collection


def build_checkpoint_flag():
    collection = ensure_collection("Prop_CheckpointFlag")
    wood = material("wood", COLORS["wood"], roughness=0.8)
    accent = material("accent_emissive", COLORS["accent"], roughness=0.4, emission=0.5)
    add_cylinder(collection, "Pole", 0.12, 8, (0, 0, 4), wood)
    flag = add_box(collection, "Flag", (2.4, 0.1, 1.5), (1.25, 0, 7), accent)
    triangulate_and_shade(flag)
    base = add_cylinder(collection, "Base", 0.8, 0.4, (0, 0, 0.2), material("stone", COLORS["stone"]))
    triangulate_and_shade(base)
    log("Prop: CheckpointFlag")
    return collection


def build_spring_pad():
    collection = ensure_collection("Prop_SpringPad")
    coil = material("spring_green", (120 / 255, 255 / 255, 120 / 255, 1.0), roughness=0.35, emission=0.5)
    for level in range(3):
        ring = add_torus(collection, f"Coil{level}", 1.0 - level * 0.18, 0.14, (0, 0, 0.3 + level * 0.35), coil)
        triangulate_and_shade(ring)
    top = add_cylinder(collection, "PadTop", 1.1, 0.2, (0, 0, 1.4), coil)
    triangulate_and_shade(top)
    log("Prop: SpringPad")
    return collection


def build_kill_brick():
    collection = ensure_collection("Prop_KillBrick")
    danger = material("danger_neon", COLORS["danger"], roughness=0.3, emission=0.9)
    brick = add_box(collection, "KillBrick", (2.2, 2.2, 1.2), (0, 0, 1), danger)
    # „kolce”: 4 małe piramidki z icosphere subdivision=0
    for corner_x, corner_y in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        spike = add_sphere(collection, f"Spike{corner_x}{corner_y}", 0.3,
                           (corner_x * 0.8, corner_y * 0.8, 1.7), danger, subdivisions=0)
        triangulate_and_shade(spike)
    triangulate_and_shade(brick)
    log("Prop: KillBrick")
    return collection


def build_moving_platform():
    collection = ensure_collection("Prop_MovingPlatform")
    gold = material("platform_gold", (255 / 255, 200 / 255, 90 / 255, 1.0), roughness=0.5)
    slab = add_box(collection, "MovingPlatform", (8, 8, 1), (0, 0, 0.5), gold)
    rail = add_box(collection, "PlatformRail", (8.4, 0.3, 0.4), (0, -3.9, 1.1),
                   material("accent_emissive", COLORS["accent"], emission=0.5))
    triangulate_and_shade(slab)
    triangulate_and_shade(rail)
    log("Prop: MovingPlatform")
    return collection


def build_tree():
    collection = ensure_collection("Prop_Tree")
    wood = material("wood", COLORS["wood"], roughness=0.85)
    leaf = material("leaf", COLORS["leaf"], roughness=0.7)
    trunk = add_cylinder(collection, "Trunk", 0.7, 5, (0, 0, 2.5), wood)
    triangulate_and_shade(trunk)
    crown = add_sphere(collection, "Crown", 2.6, (0, 0, 6.4), leaf, subdivisions=1)
    triangulate_and_shade(crown)
    log("Prop: Tree")
    return collection


def build_rock():
    collection = ensure_collection("Prop_Rock")
    stone = material("stone", COLORS["stone"], roughness=0.95)
    rock = add_sphere(collection, "Rock", 1.8, (0, 0, 0.9), stone, subdivisions=1)
    rock.scale = (1.2, 0.9, 0.8)
    rock.rotation_euler = (0, 0, math.radians(25))
    triangulate_and_shade(rock)
    log("Prop: Rock")
    return collection


def build_cloud():
    collection = ensure_collection("Prop_Cloud")
    white = material("cloud", (1.0, 1.0, 1.0, 1.0), roughness=0.9)
    for index, (x, y, z, radius) in enumerate(((-2, 0, 0, 1.6), (0, 0.4, 0.5, 2.1), (2.1, -0.2, 0, 1.5))):
        puff = add_sphere(collection, f"Puff{index}", radius, (x, y, z + 6), white, subdivisions=2)
        triangulate_and_shade(puff)
    log("Prop: Cloud")
    return collection


def build_portal_ring():
    collection = ensure_collection("Prop_PortalRing")
    accent = material("portal_neon", COLORS["accent"], roughness=0.2, emission=1.2)
    core = material("portal_core", (120 / 255, 240 / 255, 230 / 255, 1.0), roughness=0.1, emission=1.8)
    ring = add_torus(collection, "PortalRing", 5, 0.45, (0, 0, 5.5), accent, rotation=(math.pi / 2, 0, 0))
    triangulate_and_shade(ring)
    disc = add_cylinder(collection, "PortalDisc", 4.4, 0.1, (0, 0, 5.5), core, rotation=(math.pi / 2, 0, 0))
    triangulate_and_shade(disc)
    log("Prop: PortalRing")
    return collection


def build_chest():
    collection = ensure_collection("Prop_Chest")
    wood = material("wood", COLORS["wood"], roughness=0.8)
    gold = material("gold_trim", COLORS["gold"], roughness=0.3, metallic=0.8)
    body = add_box(collection, "ChestBody", (3, 2, 1.6), (0, 0, 0.8), wood)
    lid = add_box(collection, "ChestLid", (3.1, 2.1, 0.5), (0, -0.1, 1.85), wood)
    lock = add_box(collection, "ChestLock", (0.5, 0.2, 0.5), (0, -1.05, 1.35), gold)
    triangulate_and_shade(body)
    triangulate_and_shade(lid)
    triangulate_and_shade(lock)
    log("Prop: Chest")
    return collection


def build_all_props():
    builders = [
        build_coin, build_gem, build_checkpoint_flag, build_spring_pad,
        build_kill_brick, build_moving_platform, build_tree, build_rock,
        build_cloud, build_portal_ring, build_chest,
    ]
    collections = []
    for builder in builders:
        collections.append(builder())
    log(f"Propy zbudowane: {len(collections)} kolekcji.")
    return collections


if __name__ == "__main__":
    build_all_props()
