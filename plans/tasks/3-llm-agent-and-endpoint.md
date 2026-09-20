---
artifact_type: task
artifact_id: task_sahkku_phase3_llm_agent_and_endpoint_v1
task_family_id: llm-agent-and-endpoint
sequence_key: "3"
task_id: 3-llm-agent-and-endpoint
title: "LLM NPC Agent, OpenAI/LM Studio REST Client, Structured Output, and Heuristic Fallback"
status: draft
phase: phase3
target_files:
  - "Sahkku/Assets/Scripts/RulesBridge/LlmClient.cs"
  - "Sahkku/Assets/Scripts/RulesBridge/LlmPlayerAgent.cs"
  - "Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs"
  - "Sahkku/Assets/Scripts/GameLogic.cs"
  - "Sahkku/Assets/Scripts/GameSettings.cs"
  - "Sahkku/Assets/Localization/Scripts/MenuManager.cs"
  - "Sahkku/Assets/Tests/Rules/LlmAgentTests.cs"
  - "Tools/RulesTests/RulesTests.csproj"
prd_ref: null
plan_ref: plans/sahkku-architecture-plan.md
system_context_ref: null
owners:
  - "tarjeib"
doc_bubble_id: null
impl_bubble_id: null
supersedes: []
superseded_by: null
archive_group: 2026-09-19-sahkku-architecture
---

# Task: LLM NPC Agent, OpenAI/LM Studio REST Client, Structured Output, and Heuristic Fallback

## L0 - Policy

### Goal

Implement [`LlmPlayerAgent`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/LlmPlayerAgent.cs) conforming to the [`IPlayerAgent`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs) contract, backed by an OpenAI-compatible REST client ([`LlmClient`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/LlmClient.cs)) targeting local LLM servers (LM Studio, Ollama) or remote OpenAI-compatible endpoints. The agent formats game context into prompts via [`GameStateFormatter.FormatPromptContext`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/GameStateFormatter.cs#L70), requests structured JSON responses (`{"move_index": N, "reasoning": "..."}` and `{"reroll": bool, "reasoning": "..."}`), validates proposals against the engine's legal move list, and implements a deterministic fallback matrix (network error, timeout, malformed JSON, or illegal index immediately falls back to [`HeuristicPlayerAgent`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs#L106)). Wire `AgentType.LlmBot` in [`GameLogic.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameLogic.cs) and add endpoint configuration in [`GameSettings.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameSettings.cs).

### Domain / Control Model Summary

1. **Business Invariant**: The rules engine remains the sole rule and state authority. The LLM NPC only proposes decisions; every proposal is validated against `legalMoves`. Under no circumstances may an LLM response mutate [`GameState`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/Domain.cs#L116) directly or play an illegal move.
2. **Control Model**: `LlmPlayerAgent` implements `IPlayerAgent`:
   - `DecideRerollAsync(GameState state, CancellationToken cancellationToken)`
   - `DecideMoveAsync(GameState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken)`
3. **Read-Path Rule**: Prompts are generated from authoritative `GameState` projections using `GameStateFormatter.FormatPromptContext(state, legalMoves)`.
4. **Forbidden Fallback**: The agent must never perform synchronous network I/O on the main thread, never crash the match on HTTP errors/timeouts, and never return an illegal move proposal.
5. **Allowed Resolution Path**: If the LLM endpoint returns a valid JSON payload containing a legal move index `i` (`0 <= i < legalMoves.Count`), that move is resolved. Forced moves (`legalMoves.Count == 1`) skip the network query and resolve immediately.
6. **Missing-Data Rule / Fallback Matrix**: If the network times out, connection is refused, HTTP status is non-200, response JSON is unparseable, move index is out-of-range, or cancellation is requested: catch cleanly, log a structured warning, and immediately fall back to deterministic heuristic selection (`HeuristicPlayerAgent.ChooseMove` / `HeuristicPlayerAgent.ChooseReroll`).
7. **Phase Boundary**:
   - LLM NPC agent, REST client, structured JSON parser, fallback matrix, mock test harness, and GameLogic wiring: Owned here.
   - Headless CLI benchmark harness: Deferred to Task 4 (`4-cli-bench-and-eval`).

### Plan Linkage

1. **Parent plan gap closed**: Closes the LLM NPC Agent & Endpoint integration gap in [`plans/sahkku-architecture-plan.md`](file:///home/tarjeib/repo/Sahkku/plans/sahkku-architecture-plan.md).
2. **Depends on**: `2-match-controller-and-hotseat` (closed).
3. **Unlocks / impacts successors**: Unlocks Task 4 (`4-cli-bench-and-eval`) to benchmark LLM vs Heuristic bot matches headless.
4. **Inherited validation / exit expectation**: Headless test suite (`dotnet test Tools/RulesTests/RulesTests.csproj`) passes with comprehensive mock LLM tests; Unity MonoBehaviours compile cleanly.

### Scope Reality / Shape Proof

1. **Inspected entrypoints**:
   - [`Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/PlayerAgents.cs)
   - [`Sahkku/Assets/Scripts/Rules/GameStateFormatter.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/Rules/GameStateFormatter.cs)
   - [`Sahkku/Assets/Scripts/GameLogic.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameLogic.cs)
   - [`Sahkku/Assets/Scripts/GameSettings.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameSettings.cs)
   - [`Tools/RulesTests/RulesTests.csproj`](file:///home/tarjeib/repo/Sahkku/Tools/RulesTests/RulesTests.csproj)
2. **Actual touched scope**: `authority_producer` (agent move proposal) + `consumer_family_alignment` (RulesBridge + GameLogic wiring).
3. **Hidden scope ruled out**: No changes to core engine rules (`RulesEngine.cs`) or serialization formats.

### Mandatory Gates

1. **Complexity-Risk Gate**:
   - `authority_risk`: 1
   - `surface_spread`: 2 (RulesBridge, GameLogic, GameSettings, RulesTests)
   - `identity_join_risk`: 0
   - `activation_coupling`: 1
   - `prerequisite_risk`: 0
   - `acceptance_multiplicity`: 1
   - Total score: 5 (Well-bounded agent implementation and REST client integration).
2. **Authority Fan-Out Scan**:
   - `internal_execution_consumers`: `LlmPlayerAgent`, `LlmClient`.
   - `workflow_orchestration_consumers`: `GameLogic.CreateAgent`.
   - `read_model_consumers`: `GameSettings` endpoint options.
   - All other buckets: absent.
3. **Closure-Budget Gate**:
   - Collapses REST client transport, structured parser, and fallback logic inside `RulesBridge`.
   - Safe collapse: Pure C# network/parser layer tested headless via dependency-injected transport.
4. **Bounded-Task-Shape Gate**:
   - Primary shape: `consumer_family_alignment`.
   - Secondary shape: `fail_closed_hardening` (heuristic fallback on any error).

---

## L1 - Implementation Contract

### 1. REST Client & Transport Abstraction ([`Sahkku/Assets/Scripts/RulesBridge/LlmClient.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/LlmClient.cs))

```csharp
namespace Sahkku.Rules.Bridge
{
    public interface ILlmTransport
    {
        Task<string> PostChatCompletionAsync(string endpointUrl, string requestJson, CancellationToken cancellationToken);
    }

    public sealed class HttpClientLlmTransport : ILlmTransport, IDisposable
    {
        readonly HttpClient httpClient;
        readonly bool ownsHttpClient;

        public HttpClientLlmTransport(HttpClient client = null, TimeSpan? timeout = null)
        {
            ownsHttpClient = client == null;
            httpClient = client ?? new HttpClient();
            httpClient.Timeout = timeout ?? TimeSpan.FromSeconds(10);
        }

        public async Task<string> PostChatCompletionAsync(string endpointUrl, string requestJson, CancellationToken cancellationToken)
        {
            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(endpointUrl, content, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (ownsHttpClient) httpClient.Dispose();
        }
    }

    public sealed class LlmConfig
    {
        public string EndpointUrl { get; set; } = "http://localhost:1234/v1/chat/completions";
        public string ModelName { get; set; } = "pairflow-player";
        public float Temperature { get; set; } = 0.2f;
        public int MaxTokens { get; set; } = 256;
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }
}
```

### 2. Structured Output Schema & Parsing

1. **Reroll Request**:
   - System prompt instructs LLM to return JSON only:
     ```json
     {"reroll": true, "reasoning": "Want to activate soldier"}
     ```
   - Parsed into `bool reroll`: if `true` -> `RerollDecision.RerollActiveDie`, else -> `RerollDecision.KeepDiceAndProceed`.
2. **Move Request**:
   - System prompt instructs LLM to choose from numbered `## Legal moves` and return JSON:
     ```json
     {"move_index": 0, "reasoning": "Capturing enemy piece"}
     ```
   - Parsed into `int moveIndex`: validated as `0 <= moveIndex < legalMoves.Count`.
3. **JSON Extraction**:
   - Handles responses wrapped in markdown fences (` ```json ... ``` `).
   - Extracts the first `{ ... }` block if model outputs chain-of-thought or conversational preamble.

### 3. LLM Player Agent ([`Sahkku/Assets/Scripts/RulesBridge/LlmPlayerAgent.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/RulesBridge/LlmPlayerAgent.cs))

```csharp
public sealed class LlmPlayerAgent : IPlayerAgent
{
    readonly ILlmTransport transport;
    readonly LlmConfig config;
    readonly RulesEngine engine;

    public PieceOwner Owner { get; }
    public string Name { get; }

    public LlmPlayerAgent(PieceOwner owner, string name, ILlmTransport transport, LlmConfig config, RulesEngine engine)
    {
        Owner = owner;
        Name = name ?? "LLM NPC";
        this.transport = transport;
        this.config = config ?? new LlmConfig();
        this.engine = engine;
    }

    public async Task<RerollDecision> DecideRerollAsync(GameState state, CancellationToken cancellationToken)
    {
        try
        {
            string prompt = BuildRerollPrompt(state);
            string responseJson = await transport.PostChatCompletionAsync(config.EndpointUrl, BuildChatPayload(prompt), cancellationToken);
            if (TryParseReroll(responseJson, out bool reroll))
            {
                return reroll ? RerollDecision.RerollActiveDie : RerollDecision.KeepDiceAndProceed;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LogWarning($"DecideRerollAsync error: {ex.Message}; falling back to heuristic.");
        }

        return HeuristicPlayerAgent.ChooseReroll(state);
    }

    public async Task<Move> DecideMoveAsync(GameState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken)
    {
        if (legalMoves == null || legalMoves.Count == 0) return default;
        if (legalMoves.Count == 1) return legalMoves[0]; // Forced move optimization

        try
        {
            string prompt = GameStateFormatter.FormatPromptContext(state, legalMoves) + "\nChoose your move. Respond ONLY with JSON: {\"move_index\": <number>, \"reasoning\": \"<short explanation>\"}";
            string responseJson = await transport.PostChatCompletionAsync(config.EndpointUrl, BuildChatPayload(prompt), cancellationToken);
            if (TryParseMoveIndex(responseJson, legalMoves.Count, out int moveIndex))
            {
                return legalMoves[moveIndex];
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LogWarning($"DecideMoveAsync error: {ex.Message}; falling back to heuristic.");
        }

        return HeuristicPlayerAgent.ChooseMove(state, legalMoves);
    }
}
```

### 4. Settings & Match Controller Wiring

1. [`Sahkku/Assets/Scripts/GameSettings.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameSettings.cs):
   - Add static fields: `llmEndpointUrl`, `llmModelName`, `llmTimeoutSeconds`.
   - Method `GetLlmConfig()` returns a populated `LlmConfig`.
2. [`Sahkku/Assets/Scripts/GameLogic.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Scripts/GameLogic.cs):
   - In `CreateAgent`:
     ```csharp
     case GameSettings.AgentType.LlmBot:
         return new LlmPlayerAgent(owner, name, new HttpClientLlmTransport(), GameSettings.GetLlmConfig(), engine);
     ```
3. [`Sahkku/Assets/Localization/Scripts/MenuManager.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Localization/Scripts/MenuManager.cs):
   - Add options for selecting LLM Bot opponent in Play Solo / Settings.

### 5. Headless Verification ([`Sahkku/Assets/Tests/Rules/LlmAgentTests.cs`](file:///home/tarjeib/repo/Sahkku/Sahkku/Assets/Tests/Rules/LlmAgentTests.cs))

1. Implement `MockLlmTransport` with configurable responses:
   - Returns valid move JSON -> `DecideMoveAsync` picks matching `legalMoves[move_index]`.
   - Returns markdown-wrapped JSON (` ```json ... ``` `) -> parses move correctly.
   - Returns valid reroll JSON -> `DecideRerollAsync` returns chosen `RerollDecision`.
   - Returns HTTP 500 error -> falls back to `HeuristicPlayerAgent.ChooseMove` without throwing.
   - Returns timeout exception -> falls back to heuristic.
   - Returns malformed JSON -> falls back to heuristic.
   - Returns out-of-range index (e.g. index 99 when 3 moves exist) -> falls back to heuristic.
   - Single legal move -> returns move immediately without calling transport.
   - Respects `CancellationToken` cancellation.
2. Update [`Tools/RulesTests/RulesTests.csproj`](file:///home/tarjeib/repo/Sahkku/Tools/RulesTests/RulesTests.csproj) to compile new `RulesBridge` source files (`LlmClient.cs`, `LlmPlayerAgent.cs`).

---

## L2 - Hardening Backlog & Review Scope Fence

### Hardening Backlog
- Task 4: Headless CLI benchmark runner (`Tools/SahkkuBench`) executing automated bot/LLM matches.
- Streaming token parser for real-time thinking banner in Unity.

### Review Scope Fence
- Focus strictly on `LlmPlayerAgent`, `LlmClient`, structured parsing, heuristic fallback, and GameLogic wiring.
- Tests in `Tools/RulesTests` must use `MockLlmTransport` and run 100% offline without network dependencies.
