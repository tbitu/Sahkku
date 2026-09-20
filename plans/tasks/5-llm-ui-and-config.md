---
artifact_type: task
artifact_id: task_sahkku_phase5_llm_ui_and_config_v1
task_family_id: llm-ui-and-config
sequence_key: "5"
task_id: 5-llm-ui-and-config
title: "UI Interface and Shared Configuration File for OpenAI-Compatible LLM Endpoint"
status: draft
phase: phase5
target_files:
  - "Sahkku/Assets/Scripts/RulesBridge/LlmConfigFile.cs"
  - "Sahkku/Assets/Scripts/GameSettings.cs"
  - "Sahkku/Assets/Localization/Scripts/MenuManager.cs"
  - "Tools/SahkkuBench/Program.cs"
  - "Tools/SahkkuBench/SahkkuBench.csproj"
  - "Tools/RulesTests/RulesTests.csproj"
  - "Sahkku/Assets/Tests/Rules/LlmConfigTests.cs"
prd_ref: null
plan_ref: null
system_context_ref: null
owners:
  - "tarjeib"
doc_bubble_id: null
impl_bubble_id: null
supersedes: []
superseded_by: null
archive_group: 2026-09-20-sahkku-llm-ui
---

# Task: UI Interface and Shared Configuration File for OpenAI-Compatible LLM Endpoint

## L0 - Policy

### Goal

Provide a user-facing in-game UI in [`MenuManager.cs`](Sahkku/Assets/Localization/Scripts/MenuManager.cs) to view, edit, and configure the OpenAI-compatible endpoint URL and model name for the LLM NPC, and persist these settings to a shared JSON configuration file ([`llm-config.json`](llm-config.json)). Ensure that the configuration file is automatically loaded and honored by both the Unity game runtime ([`GameSettings.cs`](Sahkku/Assets/Scripts/GameSettings.cs)) and the headless match evaluation runner ([`Tools/SahkkuBench`](Tools/SahkkuBench/Program.cs)), while preserving CLI argument precedence for benchmarks. Add automated unit tests verifying serialization, loading, and fallback in [`Sahkku/Assets/Tests/Rules/LlmConfigTests.cs`](Sahkku/Assets/Tests/Rules/LlmConfigTests.cs).

### Domain / Invariant Summary

1. **Model Requirement**: The official OpenAI `/v1/chat/completions` specification requires a `"model"` parameter. While LM Studio ignores or infers it when only one model is loaded, other local or remote providers (Ollama, vLLM, OpenAI, Open-WebUI) return HTTP `400 Bad Request` if the model parameter is missing or empty. The UI and configuration file must therefore support both **Endpoint URL** (default: `http://localhost:1234/v1/chat/completions` or `http://localhost:1234/v1`) and **Model Name** (default: `pairflow-player`).
2. **Single Source of Truth / Configuration Sharing**:
   - `LlmConfigFile`: Pure C# class in `Sahkku.Rules.Bridge` (compiled across Unity, `Tools/RulesTests`, and `Tools/SahkkuBench`).
   - File format: Clean JSON (`{"endpoint": "...", "model": "..."}`).
   - Discovery order: Explicit path/argument -> `SAHKKU_LLM_CONFIG` environment variable -> upward search from binary/working directory / `Application.dataPath + "/.."` -> fallback to `Application.persistentDataPath` if root is read-only.
3. **CLI Precedence**: In `Tools/SahkkuBench`, explicitly passed CLI flags (`--endpoint`, `--model`) take precedence over the config file, which takes precedence over hardcoded defaults.
4. **Game UI Integration**:
   - In `MenuManager.cs`, when configuring game options, expose accessible input fields for endpoint URL and model name.
   - On change / edit completion, validate and update `GameSettings.llmEndpointUrl` and `GameSettings.llmModelName`, and save to `llm-config.json`.
5. **Headless Verification**: `dotnet test Tools/RulesTests/RulesTests.csproj` and `dotnet build Tools/SahkkuBench/SahkkuBench.csproj` must compile and pass cleanly without Unity dependencies.

## L1 - Architecture & Contract

### 1. `LlmConfigFile` (`Sahkku/Assets/Scripts/RulesBridge/LlmConfigFile.cs`)

- Pure C# class without `UnityEngine` references so it compiles under headless .NET 8.
- Methods:
  - `public static (string endpoint, string model) Load(string explicitPath = null)`: Loads configuration from file if present, returning defaults if not found.
  - `public static void Save(string endpoint, string model, string explicitPath = null)`: Writes formatted JSON.
  - `public static string LocateConfigFile(string explicitPath = null)`: Resolves path with upward search.
- Default values:
  - Endpoint: `http://localhost:1234/v1/chat/completions`
  - Model: `pairflow-player`

### 2. `GameSettings.cs` Updates

- At initialization or startup, `GameSettings` loads `llm-config.json` via `LlmConfigFile.Load()`.
- Updates `llmEndpointUrl` and `llmModelName` accordingly.

### 3. `MenuManager.cs` UI Integration

- Extends `AddStartOptions()` / options screen to include input fields (or dynamic UI controls) for Endpoint URL and Model Name when the options panel is displayed.
- Handles input change events to update `GameSettings` and invoke `LlmConfigFile.Save()`.

### 4. `Tools/SahkkuBench/Program.cs` Updates

- In `Options.TryParse`:
  - If `--endpoint` was not explicitly supplied, check `LlmConfigFile.Load()`.
  - If `--model` was not explicitly supplied, check `LlmConfigFile.Load()`.

### 5. Test Suite & Verification

- `Sahkku/Assets/Tests/Rules/LlmConfigTests.cs`:
  - Test serialization and deserialization of `llm-config.json`.
  - Test upward search and missing file handling (returns defaults, does not throw).
  - Test round-trip save and reload with custom endpoint and model.

## L2 - Implementation Guidance & Verification Commands

### Verification Commands

- `dotnet build Tools/RulesTests/RulesTests.csproj`
- `dotnet test Tools/RulesTests/RulesTests.csproj`
- `dotnet run --project Tools/SahkkuBench -- --games 2`
