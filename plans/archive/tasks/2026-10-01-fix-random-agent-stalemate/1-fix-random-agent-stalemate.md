---
artifact_type: task
artifact_id: task_fix_random_agent_stalemate_v1
task_family_id: fix-random-agent-stalemate
sequence_key: "1"
task_id: 1-fix-random-agent-stalemate
title: "Fix Random Agent Perpetual Reroll Stalemate and Verify Game Resolution Rules"
status: archived
phase: implementation
target_files:
  - src/agents/random.ts
  - tests/agents/player-agents.test.ts
  - src/rules/engine.ts
  - tests/rules/rules.test.ts
  - tests/cli/bench.test.ts
prd_ref: null
plan_ref: null
system_context_ref: null
owners:
  - "tarjeib"
doc_bubble_id: null
impl_bubble_id: 1-fix-random-agent-stalemate
supersedes: []
superseded_by: null
archive_group: 2026-10-01-fix-random-agent-stalemate
---

# Task 1: Fix Random Agent Perpetual Reroll Stalemate and Verify Game Resolution Rules

## L0 - Policy

### Goal
Resolve the 100% turn-limit draw anomaly in matches involving the `RandomPlayerAgent` by making its re-roll decision probabilistic rather than unconditionally re-rolling every Sáhkku face, allowing piece activations to occur and matches to terminate decisively. Verify rules engine invariants for inactive pieces, Queen capture, and turn-passing when no legal moves remain.

### Problem Summary
In `npm run bench -- --games 10 --p1 heuristic --p2 random`, 100% of matches end in draws at 10,000 half-moves with 0 wins for either side. Investigation revealed:
1. `RandomPlayerAgent.decideReroll` in `src/agents/random.ts` unconditionally returns `RerollDecision.RerollActiveDie`.
2. The match runner loops querying `decideReroll` while `canReroll(state)` holds true. Because a Sáhkku (`X`) die is the only re-rollable face, `RandomPlayerAgent` re-rolls every single Sáhkku die until it turns into a non-Sáhkku face (`three`, `two`, `zero`).
3. Consequently, the Random agent **never** retains a Sáhkku die into the movement phase.
4. Because activating inactive pieces (soldiers and the queen) requires spending a Sáhkku die, the Random agent can **never** activate any additional pieces once its 3 initial even-odds soldiers are captured.
5. In `src/rules/engine.ts`, `canLandOn` enforces `if (!top.isActive) return this.rules.inactive.enterable;` which evaluates to `false` per `SahkkuRules.json`.
6. Therefore, inactive pieces cannot be entered or captured. Once P2's (Random's) initial soldiers are eliminated, P2 is left exclusively with unactivated pieces (the Queen and 12 soldiers). P1 (Heuristic) cannot capture any of them, and P2 cannot move.
7. Both players loop indefinitely until hitting the turn cap (10,000 half-moves), resulting in 100% draws.

### Domain / Control Model Summary
1. **Business Invariant**:
   - The ruleset `SahkkuRules.json` remains authoritative: inactive pieces cannot be entered or captured (`inactive.enterable = false`), and activating an inactive piece requires spending a Sáhkku (`X`) die face (`dice.activateFace = "sahhku"`).
   - Games must terminate decisively when one player captures the enemy Queen (`landingEndsGame: true`) or exhausts all enemy soldiers (`win.opponentSoldiersExhausted: true`).
2. **Control Model**:
   - `PlayerAgent.decideReroll` governs whether an agent keeps the current thrown dice (`KeepDiceAndProceed`) or re-throws the active Sáhkku die (`RerollActiveDie`).
   - A random player agent must make random decisions, including a non-zero probability of keeping a Sáhkku die.
3. **Read-Path Rule**:
   - `RandomPlayerAgent` reads its random stream from `BotRandomSource` (or falls back to `Math.random` if unset).
4. **Forbidden Fallback**:
   - Do not bypass `RulesEngine.canLandOn` or mutate ruleset JSON invariants just to force captures of unactivated pieces.
   - Do not hardcode fixed deterministic re-roll choices inside `RandomPlayerAgent`.
5. **Allowed Resolution Path**:
   - `RandomPlayerAgent.decideReroll` draws from `this.random.nextInt(0, 2)` (or `Math.random() < 0.5`) to choose between `KeepDiceAndProceed` and `RerollActiveDie`.
6. **Missing-Data Rule**:
   - When `this.random` is null, `RandomPlayerAgent` defaults to `Math.random() < 0.5`.
7. **Phase Boundary**:
   - All changes (agent re-roll fix, rules verification, unit & integration tests) are bounded and closed within this single task.

### Scope Reality / Shape Proof
1. **Inspected entrypoints / call-sites**:
   - `src/agents/random.ts`: `RandomPlayerAgent.decideReroll`.
   - `src/cli/match-runner.ts`: `resolveRerolls` loop.
   - `src/rules/engine.ts`: `canLandOn`, `evaluateAllowedPlaces`, `nextPlayerTurn`.
   - `tests/agents/player-agents.test.ts`: `Random_AlwaysThrowsTheSahhkuAgain`.
2. **Actual touched scope**: `consumer_family_alignment` & `bugfix`.
3. **Mutation entrypoints in scope**: `RandomPlayerAgent.decideReroll`.
4. **Hidden scope ruled out**: No rules schema change, no UI changes, no serialization changes.
5. **Why the declared task shape matches reality**: The root cause is strictly located in agent re-roll behavior interacting with established rules invariants.

### Gate Detail Budget
| Gate | Detail Level | Evidence / Reason |
|---|---|---|
| Contract-Dense Gate | not_triggered | No API or schema contracts changing. |
| Refactoring Gate | not_triggered | Bugfix on agent decision logic. |
| Complexity Risk Gate | triggered_low_risk | Low surface spread across agent and test files; score 2. |
| Scoped Invariant Gate | triggered_low_risk | Invariants bounded to Random agent decision and game termination. |

### Complexity Risk Gate
1. `authority_risk`: 0 (Engine rules remain unchanged).
2. `surface_spread`: 1 (`random.ts`, test files, benchmark verification).
3. `identity_join_risk`: 0.
4. `activation_coupling`: 0.
5. `prerequisite_risk`: 0.
6. `acceptance_multiplicity`: 1 (Unit test updates + benchmark verification).
7. `risk_score`: 2.
8. `single-task allowed`: yes.

---

## L1 - Change Contract

### 0) Domain / Control Contract
| Item | Rule | Implementation Consequence | Priority | Timing |
|---|---|---|---|---|
| REQ_RANDOM_REROLL | `RandomPlayerAgent.decideReroll` must probabilistically choose between `KeepDiceAndProceed` and `RerollActiveDie`. | Use `this.random.nextInt(0, 2) === 0 ? KeepDiceAndProceed : RerollActiveDie` (or `Math.random() < 0.5` when `this.random` is null). | P1 | required-now |
| REQ_TEST_UPDATE | Update test assertions that previously pinned the unconditional re-roll behavior. | Replace `Random_AlwaysThrowsTheSahhkuAgain` with test verifying probabilistic or scripted re-roll decision. | P1 | required-now |
| REQ_DECISIVE_GAMES | Headless matches between Heuristic and Random must terminate with decisive winners within realistic half-move counts. | Verified via `npm run bench` and integration test: 0 draws over standard 10-game runs. | P1 | required-now |
| REQ_RULES_INVARIANTS | Preserve rules engine correctness: `inactive.enterable` remains false, `landingEndsGame` ends game on Queen capture. | Verified by full passing Vitest suite (`npm test`). | P1 | required-now |

### 1) Call-Site Matrix
| File | Symbol / Method | Change |
|---|---|---|
| `src/agents/random.ts` | `RandomPlayerAgent.decideReroll` | Query `this.random` for uniform 50% choice between `KeepDiceAndProceed` and `RerollActiveDie`. |
| `tests/agents/player-agents.test.ts` | `Random_AlwaysThrowsTheSahhkuAgain` | Update test to verify `RandomPlayerAgent` can return both `KeepDiceAndProceed` and `RerollActiveDie` depending on random stream. |
| `tests/cli/bench.test.ts` | Benchmark suite | Add an assertion or test case verifying that Heuristic vs Random games finish without hitting turn limits. |

### 2) Test Matrix
| Test ID | File | Scenario | Expected Outcome |
|---|---|---|---|
| T1 | `tests/agents/player-agents.test.ts` | `RandomPlayerAgent.decideReroll` with scripted random stream yielding 0 vs 1 | Returns `KeepDiceAndProceed` on 0 and `RerollActiveDie` on 1. |
| T2 | `tests/cli/bench.test.ts` | Run `MatchRunner` with Heuristic (P1) vs Random (P2) for 10 matches | All 10 matches produce decisive winners (`hitTurnLimit === false`), average half-moves < 300, 0 rule violations. |
| T3 | `tests/rules/rules.test.ts` | Existing rules suite | All 98 rules tests continue to pass without regression. |
| T4 | `npm test` | Complete Vitest test suite | 100% of tests pass across all test files. |

---

## L2 - Hardening & Notes

### Review Scope Fence
| Edge-Case Family | Why Not Required Now | Safe Current Behavior | If Discovered During Review | Route |
|---|---|---|---|---|
| Advanced Bot AI | Random agent is meant to be a baseline stochastic agent, not an intelligent player. | Uniform random choice between keep and re-roll suffices. | Document as feature for future agent tiers. | follow_up |
| Custom Variant Rules | Standard and evenOdds variants already tested and supported. | Ruleset JSON parameters govern board variants. | No changes needed to ruleset. | accepted_limitation |
