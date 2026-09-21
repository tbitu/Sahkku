---
artifact_type: task
artifact_id: task_sahkku_phase1_pipeline_and_mat_fixes_v1
task_family_id: pipeline-and-mat-fixes
sequence_key: "1"
task_id: 1-pipeline-and-mat-fixes
title: "Render Pipeline Calibration, Post-Processing Activation, and Material/Prefab Correction"
status: archived
phase: phase1
target_files:
  - "Sahkku/Assets/Scenes/Game.unity"
  - "Sahkku/Assets/Settings/SampleSceneProfile.asset"
  - "Sahkku/Assets/Settings/PC_RPAsset.asset"
  - "Sahkku/Assets/Settings/PC_Renderer.asset"
  - "Sahkku/Assets/Prefabs/KingBone.prefab"
  - "Sahkku/Assets/Prefabs/QueenBone1.prefab"
  - "Sahkku/Assets/Prefabs/QueenBone2.prefab"
  - "Sahkku/Assets/Materials/BoneSelected.mat"
  - "Sahkku/Assets/Materials/BoneSelected2.mat"
  - "Sahkku/Assets/Materials/KingRockSelected.mat"
  - "Sahkku/Assets/Materials/RockSelected.mat"
  - "Sahkku/Assets/Materials/M_Board_basecolor.png.meta"
  - "Sahkku/Assets/Materials/M_Board_Normal.png.meta"
  - "Sahkku/Assets/Materials/M_Wood.png.meta"
  - "Sahkku/Assets/Materials/M_Wood_normal.png.meta"
  - "Sahkku/Assets/Materials/M_Wood_die.png.meta"
  - "Sahkku/Assets/Materials/M_Wood_die_normal.png.meta"
  - "Sahkku/Assets/Materials/M_bone_base.png.meta"
  - "Sahkku/Assets/Materials/M_base_rock.png.meta"
  - "Sahkku/Assets/Materials/M_king_rock.png.meta"
  - "Sahkku/Assets/Materials/M_rock_normal.png.meta"
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

# Task: Render Pipeline Calibration, Post-Processing Activation, and Material/Prefab Correction

## L0 - Policy

### Goal

Eliminate blown-out linear lighting, edge aliasing, and grazing-angle texture blur by calibrating Unity's Universal Render Pipeline (URP) and post-processing profile. Fix glaring visual bugs in prefab and material assignments where bone pieces mistakenly render as untextured solid orange ([`Moss.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Moss.mat)) and selection highlight shaders swap bone and stone pieces to wood textures ([`M_Wood.png`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/M_Wood.png)). Ensure all automated tests (`dotnet test Tools/RulesTests/RulesTests.csproj`) pass cleanly with zero regressions.

### Domain / Invariant Summary

1. **Post-Processing & Color Grading**:
   - In [`Sahkku/Assets/Scenes/Game.unity`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scenes/Game.unity), GameObject `Global Volume` must be active (`m_IsActive: 1`).
   - In [`Sahkku/Assets/Settings/SampleSceneProfile.asset`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Settings/SampleSceneProfile.asset), `Tonemapping` must be enabled with ACES mode (`active: 1`, `mode.m_Value: 2`) to provide high-dynamic-range curve roll-off and prevent direct sunlight clipping.
   - `Bloom` must be enabled with subtle, high-quality parameters (`active: 1`, `intensity.m_Value: 0.15`, `threshold.m_Value: 1.0`) to give soft specular light bleed.
2. **Anti-Aliasing & Texture Quality**:
   - In [`Sahkku/Assets/Settings/PC_RPAsset.asset`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Settings/PC_RPAsset.asset), enable 4x Multi-Sample Anti-Aliasing (`m_MSAA: 4`).
   - In [`Sahkku/Assets/Settings/PC_Renderer.asset`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Settings/PC_Renderer.asset), strengthen Screen Space Ambient Occlusion (SSAO) (`Intensity: 0.8`, `Radius: 0.4`, `Samples: 8`) to anchor pieces to the board surface.
   - For all primary gameplay textures (board, pieces, dice, rock), configure anisotropic filtering (`aniso: 16`) in their respective `.meta` files to preserve crisp detail when viewed at the game's 51° camera pitch.
3. **Prefab & Material Binding Corrections**:
   - [`KingBone.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/KingBone.prefab), [`QueenBone1.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/QueenBone1.prefab), and [`QueenBone2.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/QueenBone2.prefab) must reference [`Bone.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Bone.mat) (`guid: 63f796a4a895d8245bbaedf008f5e289`), removing the placeholder reference to [`Moss.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Moss.mat).
   - In [`BoneSelected.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/BoneSelected.mat) and [`BoneSelected2.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/BoneSelected2.mat), bind `_BaseMap` to [`M_bone_base.png`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/M_bone_base.png) (`guid: fbbf3b83ae894ae40a8af1b43323484e`).
   - In [`KingRockSelected.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/KingRockSelected.mat) and [`RockSelected.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/RockSelected.mat), bind `_BaseMap` to [`M_king_rock.png`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/M_king_rock.png) (`guid: 933c6df04a3653f42ab3498976bad687`) and [`M_base_rock.png`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/M_base_rock.png) (`guid: e8ecefdec7f230248ae0637ef3937870`) respectively.
4. **Non-Breaking Invariant**:
   - All YAML files must preserve exact Unity serialization headers and GUIDs.
   - Gameplay rules, match logic, and test suites must remain unaffected.

## L1 - Architecture & Contract

### 1. Scene & Volume Profile
- [`Sahkku/Assets/Scenes/Game.unity`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scenes/Game.unity):
  - GameObject `Global Volume` (`fileID: 832575517`): set `m_IsActive: 1`.
- [`Sahkku/Assets/Settings/SampleSceneProfile.asset`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Settings/SampleSceneProfile.asset):
  - Bloom component (`fileID: -7893295128165547882`): set `active: 1`, `intensity.m_Value: 0.15`, `threshold.m_Value: 1.0`.
  - Tonemapping component (`fileID: 849379129802519247`): set `active: 1`, `mode.m_Value: 2` (ACES).

### 2. URP Settings
- [`Sahkku/Assets/Settings/PC_RPAsset.asset`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Settings/PC_RPAsset.asset):
  - Set `m_MSAA: 4`.
- [`Sahkku/Assets/Settings/PC_Renderer.asset`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Settings/PC_Renderer.asset):
  - ScreenSpaceAmbientOcclusion (`fileID: 7833122117494664109`):
    - `Intensity: 0.8`
    - `Radius: 0.4`
    - `Samples: 1` (or high quality sample count)
    - `DirectLightingStrength: 0.2`

### 3. Texture Anisotropic Filtering
In each `.meta` file:
- Set `aniso: 16` for:
  - `M_Board_basecolor.png.meta`
  - `M_Board_Normal.png.meta`
  - `M_Wood.png.meta`
  - `M_Wood_normal.png.meta`
  - `M_Wood_die.png.meta`
  - `M_Wood_die_normal.png.meta`
  - `M_bone_base.png.meta`
  - `M_base_rock.png.meta`
  - `M_king_rock.png.meta`
  - `M_rock_normal.png.meta`

### 4. Prefab Material Corrections
- [`Sahkku/Assets/Prefabs/KingBone.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/KingBone.prefab):
  - Replace `objectReference: {fileID: 2100000, guid: 836fb2206cc125b438ed43cffaf18110, type: 2}` (Moss.mat) with `{fileID: 2100000, guid: 63f796a4a895d8245bbaedf008f5e289, type: 2}` (Bone.mat).
- [`Sahkku/Assets/Prefabs/QueenBone1.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/QueenBone1.prefab) & [`Sahkku/Assets/Prefabs/QueenBone2.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/QueenBone2.prefab):
  - Replace `objectReference: {fileID: 2100000, guid: 836fb2206cc125b438ed43cffaf18110, type: 2}` (Moss.mat) with `{fileID: 2100000, guid: 63f796a4a895d8245bbaedf008f5e289, type: 2}` (Bone.mat).

### 5. Selection Material Corrections
- [`BoneSelected.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/BoneSelected.mat) & [`BoneSelected2.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/BoneSelected2.mat):
  - Set `_BaseMap` texture to `fbbf3b83ae894ae40a8af1b43323484e` (`M_bone_base.png`).
- [`KingRockSelected.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/KingRockSelected.mat):
  - Set `_BaseMap` texture to `933c6df04a3653f42ab3498976bad687` (`M_king_rock.png`).
- [`RockSelected.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/RockSelected.mat):
  - Set `_BaseMap` texture to `e8ecefdec7f230248ae0637ef3937870` (`M_base_rock.png`).

## L2 - Implementation Guidance & Verification Commands

### Verification Commands

- `dotnet build Tools/RulesTests/RulesTests.csproj`
- `dotnet test Tools/RulesTests/RulesTests.csproj`
- Python script validating YAML integrity and GUID references across modified files:
  ```bash
  python3 -c "
  import re, glob
  # Verify GUIDs exist in project
  "
  ```

### Review Closures (round 2, 2026-09-21)

- **L0 §3 / L1 §5 GUIDs corrected.** The `M_king_rock.png` and `M_base_rock.png` GUIDs previously recorded in this document (`e96a0961da8777042a9833cb79fa3a91`, `21fa12ce8e2d4234ea720516ff258169`) are declared by no `.meta` anywhere in this project, so binding them would have written dangling texture references. The GUIDs now recorded (`933c6df04a3653f42ab3498976bad687`, `e8ecefdec7f230248ae0637ef3937870`) are the ones the matching `_BaseColor` slots already referenced.
- **L0 §2 vs L1 §2 `Samples` ambiguity resolved.** `Samples: 1` is URP's `AOSampleOption.Medium`, i.e. 8 samples (`ScreenSpaceAmbientOcclusionSettings.AOSampleOption` in URP 17.x is `High` = 12, `Medium` = 8, `Low` = 4; editor tooltip "Low:4 samples, Medium: 8 samples, High: 12 samples"). L0's `Samples: 8` and L1's `Samples: 1` therefore describe the same 8-tap setting; `PC_Renderer.asset` needs no change. Cross-check: `NormalSamples: 1` is `NormalQuality.Medium` = 5 depth samples, matching the same enum family.
- **Selection material albedo slots kept consistent.** `Selected.shadergraph` declares only `BaseColor` (`_BaseColor`), `Normal` (`_Normal`), `AO` (`_AO`) and `SelectionColor`, and its `SurfaceDescription.BaseColor` block is fed from the `_BaseColor` texture property; `_BaseMap` (L1 §5) and `_MainTex` are dead slots. All texture slots that could carry albedo are bound to that piece's own texture so no bone/stone selection material can resolve to wood.
