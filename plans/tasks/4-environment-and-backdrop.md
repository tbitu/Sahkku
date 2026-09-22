---
artifact_type: task
artifact_id: task_sahkku_phase4_environment_and_backdrop_v1
task_family_id: environment-and-backdrop
sequence_key: "4"
task_id: 4-environment-and-backdrop
title: "Environment and Backdrop Presentation: PBR Tabletop Slate Ground Plane and Lighting Alignment"
status: implementable
phase: phase4
target_files:
  - "Sahkku/Assets/Materials/Ground.mat"
  - "Sahkku/Assets/Materials/RockBG.mat"
  - "Sahkku/Assets/Materials/M_Tabletop_Slate_BaseMap.png"
  - "Sahkku/Assets/Materials/M_Tabletop_Slate_BaseMap.png.meta"
  - "Sahkku/Assets/Materials/M_Tabletop_Slate_Normal.png"
  - "Sahkku/Assets/Materials/M_Tabletop_Slate_Normal.png.meta"
  - "Sahkku/Assets/Scenes/Game.unity"
  - "Sahkku/Assets/Prefabs/Ground.prefab"
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

# Task: Environment and Backdrop Presentation: PBR Tabletop Slate Ground Plane and Lighting Alignment

## L0 - Policy

### Goal

Replace the raw 2D outdoor rock photograph (`M_RockBG.jpg`) currently stretched across `Ground1` and `Ground2` in [`Sahkku/Assets/Scenes/Game.unity`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scenes/Game.unity) with a cohesive, authentic PBR tabletop / tundra slate ground plane. Calibrate the ground materials ([`Ground.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Ground.mat) and [`RockBG.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/RockBG.mat)) with authentic high-resolution PBR maps (Albedo, Normal, and calibrated Smoothness) that seamlessly integrate with the scene's dynamic Directional Light, URP Screen-Space Ambient Occlusion (SSAO), and the beveled wooden board.

### Domain / Invariant Summary

1. **Backdrop & Ground Plane Integration**:
   - Eliminate baked sun highlights and perspective distortion caused by the stretched 2D photograph.
   - Author and configure authentic PBR maps for a Nordic tabletop / slate slab surface (`M_Tabletop_Slate_BaseMap.png` and `M_Tabletop_Slate_Normal.png`) with subtle tactile texture, fine micro-grain, and a matte low-sheen finish (`Smoothness ~0.15–0.25`, `Metallic 0.0`).
   - The surface must receive real-time dynamic directional shadows and soft contact occlusion from the board and pieces via URP SSAO.
2. **Scene & Prefab Ground Consistency**:
   - Both [`RockBG.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/RockBG.mat) (used by `Ground1`/`Ground2` in `Game.unity`) and [`Ground.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Ground.mat) (used by [`Ground.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/Ground.prefab)) must be configured consistently so scene instances and prefabs render the unified tabletop slate surface.
   - The ground elevation in `Game.unity` (`y ≈ -0.70`) must sit flush immediately beneath the bottom surface of the beveled board without z-fighting or clipping piece/board colliders.
   - UV tiling scale on the ground material/mesh must provide natural texel density relative to the 4K board texture.
3. **Gameplay & Interaction Invariant**:
   - The ground plane components (`MeshCollider`, `MeshRenderer`, transforms) must not obstruct raycast hit detection for board places (`PlaceCollider`) or piece selection.
   - Headless test suite (`dotnet test Tools/RulesTests/RulesTests.csproj`) must remain 100% passing (139/139).

## L1 - Architecture & Contract

### 1. PBR Tabletop Slate Texture Contract

- **`M_Tabletop_Slate_BaseMap.png`**:
  - Resolution: 2048×2048, 8-bit sRGB PNG.
  - Surface aesthetic: Dark charcoal/slate-grey Nordic stone slab or weathered dark timber tabletop with subtle tonal variation, organic mineral veining/grain, and zero baked directional lighting or artificial drop shadows.
  - Seamlessly tileable across horizontal UV space.
- **`M_Tabletop_Slate_Normal.png`**:
  - Resolution: 2048×2048, 8-bit Linear Normal Map PNG (Unity `TextureImporterType: NormalMap`).
  - Tactile micro-relief: Subtle clefts, slate cleavage grain, or fine wood pores providing crisp specular reaction to the scene's Directional Light (`x: 35.6°, y: -29.9°`).
- **Texture Import Settings (`.meta`)**:
  - `aniso: 16` and `filterMode: 2` (Trilinear) to prevent grazing-angle blur under the isometric/orthographic player camera (`Euler: {x: 51, y: 15, z: -7}`).

### 2. Material Configuration Contract

- **[`Sahkku/Assets/Materials/RockBG.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/RockBG.mat)** and **[`Sahkku/Assets/Materials/Ground.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Ground.mat)**:
  - Shader: `Universal Render Pipeline/Lit` (`guid: 933532a4fcc9baf4fa0491de14d08ed7`).
  - `_BaseMap`: bound to `M_Tabletop_Slate_BaseMap.png`.
  - `_BumpMap`: bound to `M_Tabletop_Slate_Normal.png` with `_BumpScale: 1.0` (or calibrated subtle relief).
  - Keywords: `_NORMALMAP`.
  - `_Smoothness`: `0.15`–`0.22` (matte stone / soft satin slate finish).
  - `_Metallic`: `0.0`.
  - `_ReceiveShadows`: `1`.
  - `_EnvironmentReflections`: `1`.
  - Scale/Offset: UV tiling configured appropriately so texel density matches board resolution without repetitive tiling artifacts.

### 3. Scene Ground Alignment in `Game.unity`

- Verify `Ground1` and `Ground2` (or unified ground plane) in [`Sahkku/Assets/Scenes/Game.unity`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scenes/Game.unity):
  - Positioned at `y = -0.70` directly grounding the board.
  - `Receive Shadows: 1`, `Dynamic Occludee: 1`.
  - Seamless junction across visible camera boundaries.

## L2 - Verification Plan & Guardrails

1. **Asset Integrity Verification**:
   - Verify YAML formatting of `Game.unity`, `Ground.prefab`, `Ground.mat`, `RockBG.mat`, and new texture `.meta` files.
   - Ensure all GUID references are valid and resolve cleanly without dangling pointers or `fileID: 0`.
2. **Visual & Lighting Verification**:
   - Ground plane receives soft directional shadows from the board and pieces.
   - Contact occlusion (SSAO) darkens the perimeter boundary between the beveled board edge and the slate ground plane.
   - Zero baked sunlight angle clashes with the scene light.
3. **Headless Rules Tests**:
   - Run `dotnet test Tools/RulesTests/RulesTests.csproj`.
   - All 139 tests must pass with zero failures.
