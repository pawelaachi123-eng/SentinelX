# MEGA OBBY — generatory Blendera (modele + animacje)

Skrypty Pythona dla **Blendera 3.6 LTS i 4.x**, które budują z kodu:

- **Postać R6** (klocki jak Roblox): tors, głowa, 2 ręce, 2 nogi + armatura
  7 kości (Root → Torso → Head/Arms/Legs) z grupami wierzchołków,
- **7 animacji**: Idle, Walk, Run, Jump, Fall, Victory, Dance (30 fps, pętle
  z gładkim powtórzeniem),
- **11 propów**: Coin, Gem, CheckpointFlag, SpringPad, KillBrick,
  MovingPlatform, Tree, Rock, Cloud, PortalRing, Chest,
- **eksport FBX + GLB** do `exports/` (osobny plik na propa + rig z animacjami).

Skala: **1 stud = 0,28 m** (przeliczane automatycznie). Kolory = paleta wysp
z `src/Shared/GameConfig.lua` — spójny wygląd gry i modeli.

## Jak odpalić (3 kroki)

1. Otwórz Blender → zakładka **Scripting**.
2. **Open** → wybierz `gamedev/blender/build_all.py` → **Run Script**.
3. Zobacz w konsoli (Window → Toggle System Console) log „GOTOWE” — pliki są
   w `gamedev/blender/exports/`.

Chcesz tylko część? Osobno działają: `character.py`, `animations.py`
(wymaga postaci), `props.py`, `export.py`.

## Import do Robloxa

- **Postać**: Studio → **Avatar Setup** (lub Bulk Import) → `character_rig.fbx`.
  Rig 7 kości zostanie rozpoznany; animacje z tego samego FBX możesz wyciąć na
  KeyframeSequence (Editor → Animation Editor → Import) albo wgrać pluginem
  „Animation Importer”. Ustaw AnimationPriority: Core=movement, Action=Victory.
- **Propy**: Bulk Import `prop_*.fbx` → MeshPart; ustaw Anchored według roli
  (monety/propy dekoracyjne — CanCollide off, platforma — on).
- **Skala**: po imporcie porównaj z ludzikiem — jeśli za duży/mały, popraw
  `Size` MeshParta albo stałą `STUDS` w `lib/bpyutil.py` i wyeksportuj ponownie.

## Jak coś zmienić

| Chcesz | Zmień | Gdzie |
|---|---|---|
| kolory | paletę `COLORS` | `lib/bpyutil.py` |
| wymiary postaci | tabelę `PARTS` / `BONE_HEADS` | `character.py` |
| nowe animacje | dodaj `make_…` + wpisz do `build_all_animations` | `animations.py` |
| nowe propy | dodaj `build_…` + wpisz do `build_all_props` | `props.py` |
| format eksportu | `export.py` (`export_fbx` / `export_glb`) | `export.py` |

Uwaga uczciwa: skrypty pisane pod Blender 4.x z fallbackami 3.6 (m.in. emisyjny
materiał i nowy operator FBX). Klucze animacji wstawiam przez `keyframe_insert`
— najstabilniejsze publiczne API, działa też z nowymi „slotted actions” (4.4+).
