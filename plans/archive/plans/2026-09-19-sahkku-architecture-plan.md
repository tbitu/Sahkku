---
artifact_type: plan
artifact_id: plan_sahkku_architecture_v1
plan_id: sahkku-architecture
created_on: "2026-09-19"
title: "Sáhkku Architecture Plan: Compartmentalized Match Orchestration, Hotseat 2P, and LLM NPC"
status: done
plan_status: done
prd_ref: null
owners:
  - "tarjeib"
task_order:
  - 1-engine-decisions-and-state
  - 2-match-controller-and-hotseat
  - 3-llm-agent-and-endpoint
  - 4-cli-bench-and-eval
active_task_id: null
last_completed_task_id: 4-cli-bench-and-eval
archive_group: 2026-09-19-sahkku-architecture
task_tracker:
  - task_id: 1-engine-decisions-and-state
    task_path: plans/archive/tasks/2026-09-19-sahkku-architecture/1-engine-decisions-and-state.md
    status: archived
  - task_id: 2-match-controller-and-hotseat
    task_path: plans/archive/tasks/2026-09-19-sahkku-architecture/2-match-controller-and-hotseat.md
    status: archived
  - task_id: 3-llm-agent-and-endpoint
    task_path: plans/archive/tasks/2026-09-19-sahkku-architecture/3-llm-agent-and-endpoint.md
    status: archived
  - task_id: 4-cli-bench-and-eval
    task_path: plans/archive/tasks/2026-09-19-sahkku-architecture/4-cli-bench-and-eval.md
    status: archived
---

# Plan: Sáhkku Architecture Refactor

## Objective

Compartmentalize the Sáhkku digital game into strictly bounded architectural layers:
1. Pure C# rules engine ([`Sahkku.Rules`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs)) with explicit decision boundaries (optional rerolls, starting throw) and state serialization.
2. Match orchestration layer driving an asynchronous player agent interface ([`IPlayerAgent`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs)) with 1-player and 2-player local hotseat parity.
3. Asynchronous LLM NPC player agent communicating with local OpenAI-compatible endpoints (e.g. LM Studio on `http://localhost:1234/v1`) with robust structured output parsing, rule validation, self-correction, and heuristic fallback.
4. Headless evaluation harness verifying zero rule violations across automated bot matches without Unity.

## Done Definition

1. `dotnet test Tools/RulesTests/RulesTests.csproj` and `dotnet build Tools/RulesTests/RulesTests.csproj` pass cleanly in headless execution.
2. The rules engine explicitly models optional reroll decisions, starting player throws, and state serialization without Unity dependencies.
3. Player control is decoupled behind an asynchronous `IPlayerAgent` contract; [`GameLogic.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameLogic.cs) and [`GameInteraction.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameInteraction.cs) no longer hardcode `singlePlayer && P2` branch logic.
4. Both 1-player (Human vs Bot/LLM) and 2-player (Human vs Human local hotseat) modes function symmetrically with explicit turn banners and valid piece selection.
5. An LLM NPC can participate in matches via an OpenAI-compatible REST API, receive formatted board context, return structured moves, and have every move strictly validated by [`RulesEngine.ApplyMove`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs#L365-L374).

## Capability Closure

| Capability Claim | Closure Classification | Activation Path | Repo-Provided Boundary | External Prerequisites | Last-Mile Proof |
|---|---|---|---|---|---|
| Pure Decision & Serialization Engine | end_to_end | `RulesEngine` & `GameStateFormatter` | Rules engine methods for throw-to-start, reroll decisions, ASCII/JSON state export | .NET 8.0 SDK | Headless NUnit unit tests in `Tools/RulesTests` |
| Asynchronous Match Orchestration & Hotseat | end_to_end | `MatchController.StartMatch()` | Match state machine, turn loop, `IPlayerAgent` slots, View/Audio presenters | Unity 6 runtime | 2-player hotseat and human-vs-bot game loop in Unity |
| OpenAI/LM Studio LLM NPC Agent | end_to_end | `LlmPlayerAgent.DecideMoveAsync()` | Async prompt formatter, REST client, JSON parser, validator, and heuristic fallback | Local LM Studio server (`localhost:1234`) or OpenAI-compatible endpoint | Automated mock & live LLM test cases |
| Headless CLI Match Benchmarking | end_to_end | `dotnet run --project Tools/SahkkuBench` | Console match runner executing bot-vs-bot / LLM-vs-bot games | .NET 8.0 SDK | Headless simulated matches asserting zero `IllegalMoveException` |

## Guiding Principles & Control Model

1. **Business Invariant**: The single source of truth for all game rules is [`SahkkuRules.json`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Resources/SahkkuRules.json) interpreted by [`RulesEngine`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs). No presentation layer, AI, or network agent may bypass [`RulesEngine.ApplyMove`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/RulesEngine.cs#L365-L374) or mutate state directly.
2. **Control Model**: The Match Controller owns the authoritative match state machine. Active players are queried asynchronously through `IPlayerAgent`. Legal actions are supplied by `RulesEngine.LegalMoves(state)` and strictly checked prior to mutation.
3. **Read-Path Rule**: Prompts and UI views are built directly from authoritative [`GameState`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs#L116-L171) projections using `GameStateFormatter`.
4. **Forbidden Fallback**: The engine must never allow an illegal move even if proposed by an LLM. An invalid proposal must trigger a self-correction retry or fallback to a deterministic heuristic agent without crashing or corrupting state.
5. **Allowed Resolution Path**: Optional reroll decisions are resolved by explicit agent query (`DecideRerollAsync`) before move selection begins.
6. **Missing-Data Rule**: If an agent times out or returns malformed data, the match controller gracefully substitutes a legal heuristic move and records a warning log.
7. **Sequencing Note**:
   - Producer-first: Engine decision primitives and state formatters (Task 1) must precede Match Controller refactoring (Task 2) and LLM Agent integration (Task 3).

## Open Task List

| Task ID | Task Path | Purpose | Depends On | Closes Gap | Status |
|---|---|---|---|---|---|
| `1-engine-decisions-and-state` | `plans/archive/tasks/2026-09-19-sahkku-architecture/1-engine-decisions-and-state.md` | Add explicit reroll choice mechanics, throw-for-start integration, and state serialization to pure-C# `Sahkku.Rules`. | N/A | Decision completeness & LLM state formatting | archived |
| `2-match-controller-and-hotseat` | `plans/archive/tasks/2026-09-19-sahkku-architecture/2-match-controller-and-hotseat.md` | Refactor `GameLogic` into `MatchController` driving `IPlayerAgent` slots; implement 2P hotseat UX and `HumanPlayerAgent`. | `1-engine-decisions-and-state` | Compartmentalization & 2-player local play | archived |
| `3-llm-agent-and-endpoint` | `plans/archive/tasks/2026-09-19-sahkku-architecture/3-llm-agent-and-endpoint.md` | Implement `LlmPlayerAgent`, OpenAI/LM Studio REST client, structured JSON parser, retry loop, and heuristic fallback. | `2-match-controller-and-hotseat` | LLM NPC integration | archived |
| `4-cli-bench-and-eval` | `plans/archive/tasks/2026-09-19-sahkku-architecture/4-cli-bench-and-eval.md` | Create headless CLI tool to run and benchmark automated bot/LLM games off-engine. | `3-llm-agent-and-endpoint` | Headless verification and evaluation | archived |

## Coverage Map

| Plan Gap | Closed By | Notes |
|---|---|---|
| Optional reroll rules adherence & state serialization | `1-engine-decisions-and-state` | Pure C# engine layer |
| Decoupled match loop & 2P hotseat parity | `2-match-controller-and-hotseat` | Unity orchestration & View layer |
| Local LLM NPC integration & fault tolerance | `3-llm-agent-and-endpoint` | Agent & Network layer |
| Automated off-engine match evaluation | `4-cli-bench-and-eval` | Tooling & CI layer |
