---
artifact_type: task
artifact_id: task_sahkku_phase3_mesh_uv_unification_v1
task_family_id: mesh-uv-unification
sequence_key: "3"
task_id: 3-mesh-uv-unification
title: "Mesh Refinement, Edge Beveling, and UV Channel Consolidation"
status: archived
phase: phase3
target_files:
  - "Sahkku/Assets/Models/Board.fbx"
  - "Sahkku/Assets/Models/D4.fbx"
  - "Sahkku/Assets/Models/King.fbx"
  - "Sahkku/Assets/Models/King_Bone.fbx"
  - "Sahkku/Assets/Models/Queen_1.fbx"
  - "Sahkku/Assets/Models/Queen_2.fbx"
  - "Sahkku/Assets/Models/Queen_Bone_1.fbx"
  - "Sahkku/Assets/Models/Queen_Bone_2.fbx"
  - "Sahkku/Assets/Models/Soldier_1.fbx"
  - "Sahkku/Assets/Models/Soldier_2.fbx"
  - "Sahkku/Assets/Models/Soldier_Bone_1.fbx"
  - "Sahkku/Assets/Models/Soldier_Bone_2.fbx"
  - "Sahkku/Assets/Models/Board.fbx.meta"
  - "Sahkku/Assets/Models/D4.fbx.meta"
prd_ref: null
plan_ref: plans/2026-09-21-sahkku-graphics-overhaul-plan.md
system_context_ref: null
owners:
  - "tarjeib"
doc_bubble_id: null
impl_bubble_id: null
supersedes: []
superseded_by: null
archive_group: 2026-09-21-sahkku-graphics-overhaul
---

# Task: Mesh Refinement, Edge Beveling, and UV Channel Consolidation

## L0 - Policy

### Goal

Eliminate computational razor-sharp silhouettes and multi-channel UV misalignment across primary 3D game models (`Board.fbx`, `D4.fbx`, and the piece models). Add beveled edge chamfers to the board perimeter and pieces so specular highlights catch rim lighting under ACES tonemapping. Consolidate piece UV channels into a single unified UV0 layout matching PBR texture atlases, preventing dark occlusion bleeding and seam artifacts.

### Domain / Invariant Summary

1. **Board Mesh Chamfering**:
   - In [`Sahkku/Assets/Models/Board.fbx`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Models/Board.fbx), add clean beveled chamfers along top perimeter edges to catch specular lighting.
   - Maintain exact external boundary dimensions, pivot point at origin, and top surface plane elevation so place coordinates and raycast click selection remain 100% accurate.
2. **Piece Geometry & Edge Softening**:
   - Soften harsh low-poly faceted edges on piece silhouettes (`King.fbx`, `King_Bone.fbx`, `Queen_1.fbx`, `Queen_2.fbx`, `Queen_Bone_1.fbx`, `Queen_Bone_2.fbx`, `Soldier_1.fbx`, `Soldier_2.fbx`, `Soldier_Bone_1.fbx`, `Soldier_Bone_2.fbx`).
   - Preserve piece base footprint and overall height to ensure pieces sit cleanly within board track cells without clipping adjacent pieces.
3. **UV Channel Consolidation**:
   - Unify piece UV mapping to a single primary UV0 channel corresponding directly to the PBR texture sets (`M_Wood.png`, `M_bone_base.png`, `M_base_rock.png`).
   - Eliminate secondary UV channel desynchronization (`UVMap_base` vs `AO`) that previously produced dark occlusion blotches and seam bleeding.
4. **Preservation Invariants**:
   - FBX `.meta` files must retain their existing `guid` values so all prefabs in `Sahkku/Assets/Prefabs/` preserve their mesh references without dangling or broken links.
   - Headless test suite (`dotnet test Tools/RulesTests/RulesTests.csproj`) must remain 100% passing (139/139).

## L1 - Architecture & Contract

### 1. Model Importer & Mesh Configuration
- For each modified `.fbx`:
  - Preserve `guid` in `.meta` (e.g. `Board.fbx.meta` guid `1fc24aa748a2d984db014b2e485dace7`, `D4.fbx.meta` guid `a482b8a7fbc286c4f8d55ceec8c3b018`).
  - Keep `useFileUnits: 1`, `weldVertices: 1`, and appropriate normal smoothing angles.
  - Mesh node names within the FBX must match existing prefab `MeshFilter` bindings (or preserve default root mesh mapping).

### 2. UV0 Layout Contract
- Ensure vertex data defines UV channel 0 mapped cleanly to texture coordinate space `[0, 1]`.
- All URP Lit materials on pieces sample `_BaseMap`, `_BumpMap`, and `_OcclusionMap` via UV0 without UV channel index mismatch.

### 3. Prefab Binding Verification
- Validate all piece prefabs in `Sahkku/Assets/Prefabs/`:
  - [`King.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/King.prefab), [`KingBone.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/KingBone.prefab)
  - [`Queen1.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/Queen1.prefab), [`QueenBone1.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/QueenBone1.prefab)
  - [`Queen2.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/Queen2.prefab), [`QueenBone2.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/QueenBone2.prefab)
  - [`Soldier1.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/Soldier1.prefab), [`SoldierBone1.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/SoldierBone1.prefab)
  - [`Soldier2.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/Soldier2.prefab), [`SoldierBone2.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/SoldierBone2.prefab)
  - Verify `m_Mesh` references remain intact.

## L2 - Implementation Guidance & Verification Commands

### Verification Commands

- Execute headless rules tests:
  ```bash
  dotnet test Tools/RulesTests/RulesTests.csproj
  ```
- Validate FBX file integrity:
  ```bash
  file Sahkku/Assets/Models/*.fbx
  ```
- Verify prefab mesh references resolve cleanly without `fileID: 0` or missing GUID:
  ```bash
  python3 -c "
  import glob, re
  for p in glob.glob('Sahkku/Assets/Prefabs/*.prefab'):
      with open(p) as f:
          content = f.read()
          # Verify MeshFilter m_Mesh is valid
  "
  ```
