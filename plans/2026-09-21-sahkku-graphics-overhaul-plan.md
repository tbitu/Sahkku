---
artifact_type: plan
artifact_id: plan_sahkku_graphics_overhaul_v1
plan_id: sahkku-graphics-overhaul
created_on: "2026-09-21"
title: "Sáhkku Graphics Overhaul: PBR Materials, Mesh Refinement, and Atmospheric Presentation"
status: draft
plan_status: draft
prd_ref: null
owners:
  - "tarjeib"
task_order:
  - 1-pipeline-and-mat-fixes
  - 2-pbr-texture-upscale
  - 3-mesh-uv-unification
  - 4-environment-and-backdrop
active_task_id: 1-pipeline-and-mat-fixes
last_completed_task_id: null
archive_group: 2026-09-21-sahkku-graphics-overhaul
task_tracker:
  - task_id: 1-pipeline-and-mat-fixes
    task_path: null
    status: not_created
    notes: "Enable Global Volume post-processing (ACES tonemapping, SSAO, bloom, MSAA), anisotropic filtering, and fix prefab material assignments"
  - task_id: 2-pbr-texture-upscale
    task_path: null
    status: not_created
    notes: "Author 2K/4K PBR texture maps (curly birch wood, polished antler/bone, engraved D4 markings, proper smoothness/roughness and normal maps)"
  - task_id: 3-mesh-uv-unification
    task_path: null
    status: not_created
    notes: "Add chamfered bevels to the board and pieces, and collapse piece UVs into a unified UV0 layout for seamless PBR texture sampling"
  - task_id: 4-environment-and-backdrop
    task_path: null
    status: not_created
    notes: "Replace stretched outdoor photograph with a cohesive PBR tabletop/slate ground plane and depth-of-field presentation"
---

# Plan: Sáhkku Graphics Overhaul

## Objective

Elevate the visual presentation of Sáhkku from flat, low-poly placeholder graphics into a cohesive, high-fidelity digital tabletop experience inspired by traditional Sámi craft (*Duodji*). This involves:
1. Calibrating the Universal Render Pipeline (URP) with active post-processing (ACES tonemapping, screen-space ambient occlusion, bloom, anti-aliasing) and correcting broken material/prefab bindings.
2. Replacing flat, unshaded 1024×1024 textures with authentic 2K/4K PBR material sets (curly birch wood grain, hand-incised track grooves, polished reindeer antler/bone, and carved D4 dice faces).
3. Beveling the razor-sharp 18-vertex board and low-poly piece models to catch edge highlights, and consolidating piece UV channels to eliminate occlusion seam artifacts.
4. Integrating the board onto a cohesive, properly lit tabletop or tundra slate surface that replaces the stretched, perspectively skewed outdoor smartphone photo.

## Done Definition

1. **Rendering & Pipeline**:
   - The Global Volume in [`Game.unity`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scenes/Game.unity) is active and running ACES tonemapping and subtle bloom, preventing specular blowout and providing rich filmic dynamic range.
   - Screen-Space Ambient Occlusion (SSAO) grounds all pieces and the board with soft contact shadows.
   - 4x MSAA and 16x Anisotropic Filtering are active in [`PC_RPAsset.asset`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Settings/PC_RPAsset.asset) and texture import configurations, eliminating aliasing and grazing-angle blur.
2. **Material Integrity**:
   - [`KingBone.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/KingBone.prefab) and [`QueenBone1.prefab`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Prefabs/QueenBone1.prefab) use a dedicated bone material instead of untextured [`Moss.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/Moss.mat).
   - Selection highlight materials ([`BoneSelected.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/BoneSelected.mat), [`RockSelected.mat`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Materials/RockSelected.mat)) retain the underlying piece texture rather than reverting to wood.
3. **PBR Texture Quality**:
   - The board features authentic curly birch (*visa koivu*) grain with tactile carved track grooves, King/Queen cell markings, and a realistic satin finish (`Smoothness ~0.40–0.45`).
   - The D4 die has cleanly engraved, pigmented tally markings (I, II, III, X) without quadrant center seams.
   - Bone pieces feature subtle ivory marbling, faint porous grain, and a polished bone sheen.
4. **Mesh & UV Alignment**:
   - The board perimeter has beveled chamfers that catch specular lighting.
   - Piece meshes utilize a consolidated, single-channel UV layout (UV0) matching the PBR texture atlases, eliminating dark occlusion bleeding.
5. **Backdrop Integration**:
   - The ground plane under the board matches the 3D scene lighting, texel density, and tabletop perspective without conflicting baked sunlight.
6. **No Gameplay Regressions**:
   - The headless test harness (`dotnet test Tools/RulesTests/RulesTests.csproj`) passes cleanly with zero errors.

## Capability Closure

| Capability Claim | Closure Classification | Activation Path | Repo-Provided Boundary | External Prerequisites | Last-Mile Proof |
|---|---|---|---|---|---|
| URP Post-Processing & PBR Pipeline | end_to_end | `Game.unity` scene load via URP PC Renderer | Global Volume profile, PC RPAsset settings, SSAO renderer feature | Unity 6 / URP runtime | Visual verification in Unity Editor & player camera |
| High-Resolution PBR Material Sets | end_to_end | Material assignments in `Sahkku/Assets/Materials/` | 2K/4K PBR textures (Albedo, Normal, Metallic/Smoothness, AO) | Unity Asset Database | Visual inspection in Unity Inspector & Scene view |
| Beveled Meshes & Unified UVs | end_to_end | FBX asset import in `Sahkku/Assets/Models/` | `Board.fbx`, `D4.fbx`, piece FBXs with unified UV0 | Unity ModelImporter | FBX node inspection & piece rendering without seam artifacts |
| Tabletop Environment Presentation | end_to_end | `Ground.prefab` and background lighting in `Game.unity` | Cohesive ground material and lighting calibration | Unity 6 runtime | Player perspective camera check with dynamic shadows |

## Guiding Principles

1. **Business Invariant**: Visual and presentation enhancements must remain strictly decoupled from game rules and match logic. No mesh, material, shader, or texture modification may alter collider triggers, place indexing, raycast hit detection, or engine rules.
2. **Control Model**:
   - Visual presentation is governed by Unity scene configuration (`Game.unity`), prefab definitions (`Prefabs/`), and URP asset profiles (`Settings/`).
   - Piece selection visuals remain driven by `GameInteraction.cs` querying piece selection status through `piece.IsSelectable()`.
3. **Read-Path Rule**: Materials sample textures strictly through standard URP Lit shader properties (`_BaseMap`, `_BumpMap`, `_MetallicGlossMap`, `_OcclusionMap`) or compatible ShaderGraph interfaces.
4. **Forbidden Fallback**:
   - Do not bypass URP Lit by embedding unlit or legacy Standard shader configurations.
   - Do not stretch raw photographs across the ground without matching PBR maps and normal lighting.
   - Do not leave selection shaders swapping piece albedo textures across distinct material classes (e.g. bone turning into wood upon selection).
5. **Allowed Resolution Path**: Optional piece visual themes selected via `GameSettings.p1Model` and `GameSettings.p2Model` resolve deterministically to their respective prefab and material arrays in `GameInteraction`.
6. **Missing-Data Rule**: Any material property missing an explicit texture map must fall back to neutral PBR defaults (Occlusion: 1.0/white, Smoothness: 0.4, Metallic: 0.0) rather than pitch black or zero-specular chalk.
7. **Sequencing / Boundary Note**:
   - **Phase 1 (Pipeline & Material Fixes)** must come first to establish correct color reproduction, lighting, and eliminate known prefab bugs.
   - **Phase 2 (PBR Texture Overhaul)** establishes the visual aesthetic and surface details.
   - **Phase 3 (Mesh & UV Alignment)** unifies the geometry and UV layouts so PBR textures map without distortion.
   - **Phase 4 (Backdrop & Environment)** completes scene framing and background cohesion.

## Current Status

### Completed Work
- Visual diagnostics and asset inspection completed across all materials, models, and URP configuration.
- Identified root causes: inactive Global Volume, disabled MSAA/anisotropic filtering, broken `M_board_AO.png`, multi-UV occlusion sampling mismatch on pieces, missing bone materials on `KingBone`/`QueenBone`, and perspective photo ground plane.

### Open Work
- Task 1: Activate Global Volume, enable ACES tonemapping/bloom/SSAO, set 4x MSAA and 16x aniso, fix prefab material overrides and selection shader bindings.
- Task 2: Author high-res PBR texture sets (curly birch, bone/antler, carved D4, board markings) with accurate roughness/smoothness and normal maps.
- Task 3: Add chamfered bevels to board and piece meshes, consolidate piece UVs into unified UV0 layout.
- Task 4: Replace stretched photo ground plane with an authentic PBR tabletop or tundra slate slab.

### Deferred / Future Work
- Dynamic camera framing animations during dice throw or capture events.
- Additional cosmetic theme unlocks (e.g. carved soapstone, oxidized bronze, winter snow ground).

## Open Task List

| Task ID | Task Path | Purpose | Depends On | Closes Gap | Status |
|---|---|---|---|---|---|
| `1-pipeline-and-mat-fixes` | `plans/tasks/2026-09-21-sahkku-graphics-overhaul/1-pipeline-and-mat-fixes.md` | Activate URP Global Volume (ACES tonemapping, bloom, SSAO), configure 4x MSAA / 16x anisotropic filtering, fix `KingBone`/`QueenBone` prefab material assignments, and fix bone/stone selection materials. | N/A | Lighting blowout, aliasing, and broken prefab material bindings | not_created |
| `2-pbr-texture-upscale` | `plans/tasks/2026-09-21-sahkku-graphics-overhaul/2-pbr-texture-upscale.md` | Author 2K/4K PBR texture maps (albedo, normal, smoothness, occlusion) for curly birch board, reindeer bone pieces, and carved D4 dice without texture seams. | `1-pipeline-and-mat-fixes` | Flat yellow placeholder look, missing roughness/normal detail, and D4 seam | not_created |
| `3-mesh-uv-unification` | `plans/tasks/2026-09-21-sahkku-graphics-overhaul/3-mesh-uv-unification.md` | Add chamfered edge bevels to `Board.fbx` and piece models; unify multi-channel UV layouts (`UVMap_base` and `AO`) into single clean UV0 mapping. | `2-pbr-texture-upscale` | Sharp low-poly computational silhouettes and multi-UV occlusion artifacts | not_created |
| `4-environment-and-backdrop` | `plans/tasks/2026-09-21-sahkku-graphics-overhaul/4-environment-and-backdrop.md` | Replace raw 2D outdoor photo ground plane with a cohesive PBR tabletop or tundra slate slab with matching dynamic shadows and camera depth-of-field. | `3-mesh-uv-unification` | Background scale mismatch, baked lighting conflict, and scene immersion | not_created |

## Coverage Map

| Plan Gap | Closed By | Notes |
|---|---|---|
| Blown-out linear lighting & flat contrast | `1-pipeline-and-mat-fixes` | ACES tonemapping and calibrated volume profile in URP |
| Severe grazing-angle blur and jagged edges | `1-pipeline-and-mat-fixes` | 16x anisotropic filtering and 4x MSAA in PC RPAsset |
| KingBone / QueenBone untextured orange appearance | `1-pipeline-and-mat-fixes` | Prefab material override fix from `Moss.mat` to `Bone.mat` |
| Selection highlight swapping bone/stone to wood | `1-pipeline-and-mat-fixes` | Correct `_BaseMap` bindings in selection materials |
| Flat yellow paint & lack of wood grain on board/pieces | `2-pbr-texture-upscale` | 4K curly birch PBR texture set with incised carved track lines |
| Muddy 512×512 bone textures without normal or AO | `2-pbr-texture-upscale` | 2K reindeer antler/bone PBR texture set with polished ivory sheen |
| D4 dice quadrant seam and soft brush marks | `2-pbr-texture-upscale` | Seamless D4 PBR maps with crisp incised markings |
| 18-vertex box board and harsh low-poly piece silhouettes | `3-mesh-uv-unification` | Chamfered/beveled edge geometry catching specular highlights |
| Dark smudge artifacts from mismatched UV channels | `3-mesh-uv-unification` | Unified UV0 layout mapping albedo, normal, and AO consistently |
| Stretched 2D phone photo conflicting with 3D scene lighting | `4-environment-and-backdrop` | Cohesive PBR tabletop / slate surface with matching lighting and shadows |

## Dependencies and Order

1. **Task 1 must precede Task 2**: Calibrating the rendering pipeline (ACES tonemapping, correct material slots, SSAO) ensures that new textures can be evaluated under accurate lighting conditions without color clipping.
2. **Task 2 precedes Task 3**: Finalizing the PBR texture resolution and atlas layout guides the exact UV island boundaries and bevel geometry for the 3D meshes.
3. **Task 3 precedes Task 4**: Finalizing the board and piece scale/geometry ensures the environment ground plane and camera depth-of-field align seamlessly with board bounds.

## Risks and Assumptions

1. **Unity Asset Database Reimport**:
   - *Risk*: Modifying `.meta` files and `.fbx` files outside the Unity editor may require Unity to reimport assets when opened in the GUI.
   - *Mitigation*: Maintain valid Unity YAML serialization format across all `.mat`, `.prefab`, `.asset`, and `.meta` changes so all GUID references remain strictly intact.
2. **Rules & Interaction Preservation**:
   - *Risk*: Modifying piece or board geometry could inadvertently affect `MeshCollider` bounds or raycasting coordinates.
   - *Mitigation*: Ensure `MeshCollider` components retain functional raycast boundaries or utilize simplified convex collision shapes matching original footprints.
3. **Web / Mobile Performance**:
   - *Risk*: 4K textures and 4x MSAA could impact WebGL or mobile performance.
   - *Mitigation*: Texture max sizes can be scaled per platform in `.meta` files (e.g. 2048 for Desktop/PC, 1024 for WebGL/Mobile), and URP assets already maintain separate `PC_RPAsset` and `Mobile_RPAsset` profiles.

## Validation Strategy

1. **Automated Headless Test Suite**:
   - Execute `dotnet test Tools/RulesTests/RulesTests.csproj` after each task to ensure rules engine and bridge logic remain 100% regression-free.
2. **Asset Integrity Checks**:
   - Validate YAML syntax of all modified `.mat`, `.asset`, and `.prefab` files.
   - Verify all GUID references resolve to existing assets without broken or missing references (`fileID: 0` or missing GUIDs).
3. **Visual & Rendering Quality Verifications**:
   - Inspect rendered outputs in the player camera view for specular highlight catch on beveled edges.
   - Verify smooth contact shadowing under pieces and board via SSAO.
   - Verify zero seam artifacts or coordinate bleeding on piece and dice textures.
