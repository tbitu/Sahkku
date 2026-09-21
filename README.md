# Sáhkku (Digital Sáhkku)

Play in browser: [itch.io](https://zhamul.itch.io/digital-shkku)

Sáhkku is a traditional Sámi running-fight board game with centuries of history. This repository contains a digital implementation built in Unity with an engine-agnostic, pure-C# rules core.

---

## Features Added Since Fork

- **Pure-C# Rules Engine & Headless Verification**:
  - Decoupled, JSON-driven rules engine ([`Sahkku/Assets/Resources/SahkkuRules.json`](Sahkku/Assets/Resources/SahkkuRules.json)) with figure-of-eight board track traversal, move validation, and game state invariant enforcement.
  - Headless test suite ([`Tools/RulesTests`](Tools/RulesTests/)) verifying all rules without Unity via standard `dotnet test`.

- **Modular Player Agents & Local 2P Hotseat**:
  - Decoupled match controller supporting pluggable agents: Human, Random, Rule-Based (greedy heuristic), and LLM.
  - Local 2-Player hotseat mode and starting player selection from the menu.

- **OpenAI-Compatible LLM Player Agent**:
  - REST client supporting any OpenAI-compatible `/v1/chat/completions` endpoint (LM Studio, Ollama, vLLM, OpenAI).
  - Translates game state and legal moves into structured LLM prompts, parsing JSON move choices with automatic fallback validation.

- **In-Game Settings & Shared Configuration**:
  - In-game options UI in `MenuManager` to view and configure LLM endpoint URL and model name (with multi-language localization).
  - Shared `llm-config.json` configuration file ([`Sahkku/Assets/Scripts/RulesBridge/LlmConfigFile.cs`](Sahkku/Assets/Scripts/RulesBridge/LlmConfigFile.cs)) synced across the Unity runtime and CLI tools.

- **Headless CLI Match Runner & Benchmarks ([`Tools/SahkkuBench`](Tools/SahkkuBench/))**:
  - Headless CLI harness to run automated matches, statistics, and evaluation benchmarks between arbitrary agent pairings.

---

## Quick Start (Headless CLI)

Run unit tests:
```bash
dotnet test Tools/RulesTests/RulesTests.csproj
```

Run CLI benchmark matches:
```bash
# Run 10 matches between Rule-Based and Random agents
dotnet run --project Tools/SahkkuBench -- --p1 rule --p2 random --games 10

# Run a match against an LLM player
dotnet run --project Tools/SahkkuBench -- --p1 rule --p2 llm --endpoint http://localhost:1234/v1/chat/completions --model pairflow-player
```

---

## Notes & Known Quirks

- **Engine & Toolchain**: Made with Unity 6 (6.0000.6.0f1); headless tools and test suite target .NET 8.
- **Rules Documentation**: See [`Docs/rules-engine.md`](Docs/rules-engine.md) for the ruleset schema and [`Docs/rules-alignment.md`](Docs/rules-alignment.md) for how the engine maps onto traditional printed rules.
- **Dice Reroll**: Dice are rerolled one at a time because dice are re-sorted after each roll.
- **Starting Player**: The menu allows picking the starting player; throwing for start is implemented in the engine (`RulesEngine.ThrowForStartingPlayer`) but not active in the UI.
- **Even Odds**: "Like odds" marks the three foremost soldiers loose in place (see [`Docs/rules-alignment.md`](Docs/rules-alignment.md)).
- **PC Builds**: When building for PC from Unity, manually copy the rules PDF files to the root of the build.
