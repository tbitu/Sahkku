---
artifact_type: task
artifact_id: task_sahkku_phase2_pbr_texture_upscale_v1
task_family_id: pbr-texture-upscale
sequence_key: "2"
task_id: 2-pbr-texture-upscale
title: "PBR Texture Upscale: Curly Birch Board, Reindeer Bone Pieces, and Carved D4 Die"
status: implementable
phase: phase2
target_files:
  - "Sahkku/Assets/Materials/M_Board_basecolor.png"
  - "Sahkku/Assets/Materials/M_Board_Normal.png"
  - "Sahkku/Assets/Materials/Board.mat"
  - "Sahkku/Assets/Materials/M_bone_base.png"
  - "Sahkku/Assets/Materials/M_bone_normal.png"
  - "Sahkku/Assets/Materials/M_bone_normal.png.meta"
  - "Sahkku/Assets/Materials/Bone.mat"
  - "Sahkku/Assets/Materials/Bone2.mat"
  - "Sahkku/Assets/Materials/BoneSelected.mat"
  - "Sahkku/Assets/Materials/BoneSelected2.mat"
  - "Sahkku/Assets/Materials/M_Wood_die.png"
  - "Sahkku/Assets/Materials/M_Wood_die_normal.png"
  - "Sahkku/Assets/Materials/D4.mat"
  - "Sahkku/Assets/Materials/D4Highlight.mat"
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

# Task: PBR Texture Upscale: Curly Birch Board, Reindeer Bone Pieces, and Carved D4 Die

## L0 - Policy

### Goal

Eliminate flat, chalky, unshaded placeholder textures across primary gameplay assets (board, reindeer bone pieces, and D4 dice) by authoring high-resolution 2K/4K PBR texture sets (Albedo and Normal maps) and calibrating URP Lit material parameters (smoothness, normal map binding, specular response). Eliminate the visual quadrant center seam on the D4 dice faces and provide distinct, authentic *Duodji* artisanal tactile quality (curly birch wood grain, incised track lines, polished antler sheen).

### Domain / Invariant Summary

1. **Board Texture & PBR Material**:
   - `M_Board_basecolor.png` (2048×2048 or 4096×4096): Replace uniform yellow wash with authentic curly birch (*visa koivu*) wood grain, subtle mineral streaks, distinct hand-incised track lines, and crisp King sanctuary demarcation.
   - `M_Board_Normal.png` (2048×2048 or 4096×4096): Depth-matched normal map capturing wood grain micro-ridges, tactile track grooves, and perimeter bevel depth.
   - In [`Sahkku/Assets/Materials/Board.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Board.mat), set `_Smoothness: 0.42` (satin finish reflecting soft environment light) rather than `_Smoothness: 0.0` (matte chalk).
2. **Reindeer Bone Pieces**:
   - `M_bone_base.png`: Upscale/re-author from blurry 512×512 to 2048×2048 with ivory/antler tones, subtle natural grain, and gentle wear patina.
   - `M_bone_normal.png` (2048×2048): New normal map capturing longitudinal bone grain, polished carved facets, and surface tactile depth. Add matching `M_bone_normal.png.meta` with `textureType: 1` (Normal map), `sRGBTexture: 0`, and `aniso: 16`.
   - In [`Sahkku/Assets/Materials/Bone.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Bone.mat) and [`Sahkku/Assets/Materials/Bone2.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Bone2.mat): bind `_BumpMap` to `M_bone_normal.png`, enable keyword `_NORMALMAP`, and calibrate `_Smoothness: 0.58` (polished antler sheen).
   - In [`Sahkku/Assets/Materials/BoneSelected.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/BoneSelected.mat) and [`Sahkku/Assets/Materials/BoneSelected2.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/BoneSelected2.mat): bind `_Normal` texture slot to `M_bone_normal.png`.
3. **D4 Dice**:
   - `M_Wood_die.png` (2048×2048) & `M_Wood_die_normal.png` (2048×2048): Author seamless textures without quadrant center seams or soft airbrush artifacts. Feature cleanly incised, pigmented Sáhkku tally numerals (I, II, III, X) with beveled normal depth.
   - In [`Sahkku/Assets/Materials/D4.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/D4.mat) and [`Sahkku/Assets/Materials/D4Highlight.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/D4Highlight.mat), calibrate `_Smoothness: 0.45` to catch specular edge lighting.
4. **Preservation Invariants**:
   - Texture dimensions and aspect ratios must remain compatible with existing UV layouts.
   - All Unity YAML serialization headers, GUIDs, and file structure must remain valid.
   - Headless test suite (`dotnet test Tools/RulesTests/RulesTests.csproj`) must pass with zero errors.

## L1 - Architecture & Contract

### 1. Board Texture & Material Calibration
- Texture: [`Sahkku/Assets/Materials/M_Board_basecolor.png`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/M_Board_basecolor.png) (2048×2048, sRGB).
- Texture: [`Sahkku/Assets/Materials/M_Board_Normal.png`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/M_Board_Normal.png) (2048×2048, Linear/NormalMap).
- Material: [`Sahkku/Assets/Materials/Board.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Board.mat):
  - `_Smoothness: 0.42`
  - `_SpecularHighlights: 1`
  - `_EnvironmentReflections: 1`
  - `_ValidKeywords`: ensure `_NORMALMAP` is active.

### 2. Bone Material & Normal Map Addition
- Texture: [`Sahkku/Assets/Materials/M_bone_base.png`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/M_bone_base.png) (2048×2048, sRGB).
- New Texture: [`Sahkku/Assets/Materials/M_bone_normal.png`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/M_bone_normal.png) (2048×2048, Linear/NormalMap).
- New Meta: [`Sahkku/Assets/Materials/M_bone_normal.png.meta`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/M_bone_normal.png.meta):
  - `textureType: 1`
  - `sRGBTexture: 0`
  - `aniso: 16`
  - Assign a unique 32-character hex GUID (e.g. `c7e3f89a4b12d5e6f0a1b2c3d4e5f601`).
- Materials: [`Bone.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Bone.mat) & [`Bone2.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Bone2.mat):
  - `_BumpMap`: bind texture to `M_bone_normal.png` GUID.
  - `_BumpScale: 1.0`
  - `_Smoothness: 0.58`
  - `_ValidKeywords`: add `- _NORMALMAP`.
- Selection Materials: [`BoneSelected.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/BoneSelected.mat) & [`BoneSelected2.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/BoneSelected2.mat):
  - Bind `_Normal` texture slot to `M_bone_normal.png` GUID.

### 3. D4 Dice Seamless Authoring
- Texture: [`Sahkku/Assets/Materials/M_Wood_die.png`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/M_Wood_die.png) (2048×2048, sRGB).
- Texture: [`Sahkku/Assets/Materials/M_Wood_die_normal.png`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/M_Wood_die_normal.png) (2048×2048, Linear/NormalMap).
- Materials: [`D4.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/D4.mat) & [`D4Highlight.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/D4Highlight.mat):
  - `_Smoothness: 0.45`
  - Verify `_BumpMap` binds to `M_Wood_die_normal.png` (`guid: 1abac3e027bd35f42945654c014e7872`).

## L2 - Implementation Guidance & Verification Commands

### Verification Commands

- Execute headless rules test:
  ```bash
  dotnet test Tools/RulesTests/RulesTests.csproj
  ```
- Verify texture image integrity and dimensions:
  ```bash
  file Sahkku/Assets/Materials/M_Board_basecolor.png Sahkku/Assets/Materials/M_Board_Normal.png Sahkku/Assets/Materials/M_bone_base.png Sahkku/Assets/Materials/M_bone_normal.png Sahkku/Assets/Materials/M_Wood_die.png Sahkku/Assets/Materials/M_Wood_die_normal.png
  ```
- Validate Unity YAML references and GUID integrity across modified material and meta files:
  ```bash
  python3 -c "
  import yaml, glob
  # Ensure all modified .mat files have valid syntax and resolve referenced GUIDs
  "
  ```
