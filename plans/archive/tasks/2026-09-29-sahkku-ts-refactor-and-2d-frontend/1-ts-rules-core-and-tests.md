---
artifact_type: task
artifact_id: task_1_ts_rules_core_and_tests_v1
task_family_id: ts-rules-core-and-tests
sequence_key: "1"
task_id: 1-ts-rules-core-and-tests
title: "Port Pure Rules Core and Test Suite (139 Tests) to TypeScript and Vitest"
status: archived
phase: implementation
target_files:
  - package.json
  - tsconfig.json
  - vitest.config.ts
  - src/rules/domain.ts
  - src/rules/ruleset.ts
  - src/rules/track.ts
  - src/rules/engine.ts
  - src/rules/formatter.ts
  - src/rules/SahkkuRules.json
  - tests/rules/rules.test.ts
prd_ref: null
plan_ref: plans/2026-09-29-sahkku-ts-refactor-and-2d-frontend-plan.md
system_context_ref: null
owners:
  - "tarjeib"
doc_bubble_id: null
impl_bubble_id: 1-ts-rules-core-and-tests
supersedes: []
superseded_by: null
archive_group: 2026-09-29-sahkku-ts-refactor-and-2d-frontend
---

# Task 1: Port Pure Rules Core and Test Suite (139 Tests) to TypeScript and Vitest

## L0: Context & Scope

### Problem Summary
The existing Sáhkku rules engine is implemented in C# ([`Sahkku.Rules`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs)) with tests executing via NUnit and .NET 8 SDK. To achieve a complete refactor toward an open-source, non-3D web frontend without proprietary Unity dependencies, we must port the entire pure rules engine to strict TypeScript. The ported engine must maintain 100% fidelity to the canonical ruleset ([`SahkkuRules.json`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Resources/SahkkuRules.json)) and achieve 100% test parity across all 139 existing test cases in Vitest.

### Scope Reality Proof
- **Inspected C# Source Files**:
  - [`Sahkku/Assets/Scripts/Rules/Domain.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs): Data structures, enums, `GameState`, `Piece`, `Place`, `Move`, `RuleEvent`.
  - [`Sahkku/Assets/Scripts/Rules/RuleSet.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RuleSet.cs): JSON schema validation and rule definitions.
  - [`Sahkku/Assets/Scripts/Rules/Track.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Track.cs): Figure-of-8 track coordinate mapping and arcs.
  - [`Sahkku/Assets/Scripts/Rules/RulesEngine.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs): Pure game rules engine logic and invariants.
  - [`Sahkku/Assets/Scripts/Rules/GameStateFormatter.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/GameStateFormatter.cs): Board ASCII and prompt context formatting.
  - [`Sahkku/Assets/Tests/Rules/RulesTests.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Tests/Rules/RulesTests.cs): 139 NUnit unit tests.
- **Bounded Slice**:
  - Initialize root `package.json`, `tsconfig.json`, and `vitest.config.ts`.
  - Copy [`SahkkuRules.json`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Resources/SahkkuRules.json) to `src/rules/SahkkuRules.json`.
  - Implement `src/rules/domain.ts`, `src/rules/ruleset.ts`, `src/rules/track.ts`, `src/rules/engine.ts`, and `src/rules/formatter.ts`.
  - Implement comprehensive Vitest test suite in `tests/rules/rules.test.ts` verifying all 139 test cases with zero regressions.

### Scoped Invariants
- `applies_to`: `src/rules/*`, `tests/rules/*`, `package.json`, `tsconfig.json`, `vitest.config.ts`.
- `does_not_apply_to`: Frontend UI components, web sound effects, legacy Unity folders.
- `proof_surface`: `npm test` runs in Vitest and passes all 139 ported rule tests cleanly.

---

## L1: Implementation Contract

### 1. Functional Requirements

- **REQ_WORKSPACE_INIT**: Create root `package.json` with scripts (`"test": "vitest run"`), configuring TypeScript 5+ (`tsconfig.json`) in strict mode with ES2022 module resolution.
- **REQ_DOMAIN_TYPES**: In `src/rules/domain.ts`, define:
  - Enums: `PieceType` (Soldier, King, Queen), `PieceOwner` (None, P1, P2), `DieFace` (Sahhku, Three, Two, Zero), `TurnPhase` (P1roll, P1move, P2roll, P2move), `RerollDecision` (KeepDiceAndProceed, RerollActiveDie), `WinReason` (None, OpponentSoldiersExhausted, QueenCaptured), `RuleEventKind`.
  - Classes/Interfaces: `Piece`, `Place`, `GameState`, `Move`, `RuleEvent`, `EngineOptions`, `IRandomSource`.
  - Methods: `Piece.isSelectable()`, `GameState.clone()`, `GameState.currentPlayer`, `GameState.isRollPhase`.
- **REQ_RULESET_VALIDATION**: In `src/rules/ruleset.ts`, import `SahkkuRules.json` and validate schema invariants via `RuleSet.validate()`:
  - Board dimensions (15x3, `rowMajorSnake`).
  - Track legs join end-to-end with continuous bends forming the figure-of-8.
  - Dice spending order and face values (`sahhku: 1`, `three: 3`, `two: 2`, `zero: 0`).
  - Activation queue offset (`-1`).
  - Pieces primitives (soldier, queen, king).
- **REQ_TRACK_MATH**: In `src/rules/track.ts`, implement track coordinate math:
  - `trackLength` (`4 * board.width` = 60).
  - `placeOfArc(owner: PieceOwner, arc: number): number`.
  - `arcOfPlace(owner: PieceOwner, place: number): number`.
  - `homeRowOf(owner: PieceOwner): number`.
- **REQ_RULES_ENGINE**: In `src/rules/engine.ts`, implement pure `RulesEngine` methods:
  - `initGame(options: EngineOptions, random?: IRandomSource): GameState`.
  - `throwForStartingPlayer(random: IRandomSource): PieceOwner`.
  - `rollAllDice(state: GameState, random: IRandomSource): void`.
  - `rollAndBeginTurn(state: GameState, random: IRandomSource): void`.
  - `orderDice(state: GameState): void`.
  - `rerollFirstDie(state: GameState, random: IRandomSource): void`.
  - `canReroll(state: GameState): boolean`.
  - `applyRerollDecision(state: GameState, decision: RerollDecision, random: IRandomSource): void`.
  - `evaluateAllowedPlaces(state: GameState): boolean`.
  - `getLegalMoves(state: GameState): Move[]`.
  - `legalMoves(state: GameState): Move[]`.
  - `isLegalMove(state: GameState, move: Move): boolean`.
  - `getAllowedPlaces(state: GameState, piece: Piece): number[]`.
  - `getAllowedArcs(state: GameState, piece: Piece): number[]`.
  - `applyMove(state: GameState, move: Move): RuleEvent[]` (throws `IllegalMoveException` on illegal move).
  - `tryApplyMove(state: GameState, move: Move): { success: boolean; events: RuleEvent[] }`.
  - `nextPlayerTurn(state: GameState): void`.
  - `validateState(state: GameState): string[]`.
- **REQ_STATE_FORMATTER**: In `src/rules/formatter.ts`, implement `GameStateFormatter`:
  - `formatBoard(state: GameState): string` (3-row ASCII grid).
  - `formatDice(state: GameState): string`.
  - `formatPromptContext(state: GameState, legalMoves: Move[]): string`.
- **REQ_TEST_PARITY**: Port all 139 tests from `RulesTests.cs` into `tests/rules/rules.test.ts`:
  - Shipped ruleset validation and schema checks.
  - Track figure-of-8 traversal, second middle-row pass, return home, mirror track.
  - Standard and Even-Odds (`liká`) starting setups.
  - Soldier movement, forward-only constraint, activation queue unlocking.
  - Inactive soldier line protection and enemy entry blockage.
  - Queen movement (forward, backward, vertical) and instant win on queen capture.
  - King movement, bend transitions, and recruitment (by landing on neutral king or soldier reaching enemy home row).
  - Dice spending order (X -> III -> II -> 0) and optional reroll rules.
  - State invariant validation checks.
  - Two seeded full-game simulations with deterministic move parity.

### 2. Call-Site Matrix

| Target File | Method / Entity | Description |
|---|---|---|
| `package.json` | root configuration | Define dependencies (`vitest`, `typescript`), scripts (`test`). |
| `src/rules/domain.ts` | types & `GameState` | Core data model, enums, immutable state clone. |
| `src/rules/ruleset.ts` | `RuleSet` | Ruleset loading and validation logic. |
| `src/rules/track.ts` | `BoardTrack` | Figure-of-8 track coordinate calculations. |
| `src/rules/engine.ts` | `RulesEngine` | Pure decision engine and state transition functions. |
| `src/rules/formatter.ts` | `GameStateFormatter` | ASCII board rendering and prompt context serialization. |
| `tests/rules/rules.test.ts` | Vitest suite | Ported 139 test cases asserting exact behavioral parity. |

### 3. Error and Fallback Handling

- Throw `IllegalMoveException` if an illegal move is passed to `applyMove`.
- Throw `RuleSetException` if `SahkkuRules.json` fails schema validation or invariant checks.
- Pure functions return empty arrays rather than throwing on normal "no moves available" conditions.

### 4. Test Matrix

| Test ID | Input / Scenario | Expected Outcome |
|---|---|---|
| `T1_TS_RULES_PARITY` | `npm test` | All 139 ported rule tests pass in Vitest. |
| `T1_RULESET_VALIDATION` | Parse `SahkkuRules.json` | Zero validation errors, all schema fields recognized. |
| `T1_DETERMINISTIC_SIM` | Seeded game simulations | Sim 1 and Sim 2 match exact winner and turn count. |

---

## L2: Hardening Backlog

- Add TypeScript export declarations (`index.d.ts`) if publishing as an independent npm package.
- Add micro-benchmarks measuring move evaluation throughput per millisecond.
