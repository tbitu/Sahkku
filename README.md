# Sáhkku (Digital Sáhkku)

A digital implementation of **Sáhkku**, a traditional Sámi running-fight board game with centuries of history. Built with Unity and powered by an engine-agnostic, pure-C# rules core.

Play in browser: [itch.io](https://zhamul.itch.io/digital-shkku)

---

## Features

- **Pure-C# Rules Engine**: A decoupled, JSON-driven ruleset ([`SahkkuRules.json`](Sahkku/Assets/Resources/SahkkuRules.json)) supporting figure-of-eight board track traversal, move validation, and game state invariant enforcement.
- **Multiple Game Modes**: Local 2-player hotseat and single-player matches against autonomous player agents.
- **Pluggable Player Agents**:
  - **Human**: Interactive GUI or CLI input.
  - **Random**: Baseline randomized move selection.
  - **Rule-Based**: Heuristic greedy agent evaluating captures, piece advancement, and king mechanics.
  - **LLM Agent**: Integrates with any OpenAI-compatible `/v1/chat/completions` endpoint (LM Studio, Ollama, vLLM, OpenAI).
- **In-Game Settings & Localization**: In-game menu for configuring LLM endpoints and model names, backed by a shared `llm-config.json` configuration file synced between Unity and headless CLI tools.
- **Headless Benchmarking & CLI Runner (`SahkkuBench`)**: Run simulated matches and evaluation benchmarks headlessly at high speed without launching Unity.
- **Headless Test Suite**: 100% of the game rules are verified independently of Unity via standard .NET unit tests.

---

## Quick Start

### Run Unit Tests
```bash
dotnet test Tools/RulesTests/RulesTests.csproj
```

### Run Headless Benchmarks & Matches
```bash
# Run 10 matches between Rule-Based and Random agents
dotnet run --project Tools/SahkkuBench -- --p1 rule --p2 random --games 10

# Play a match against an LLM endpoint
dotnet run --project Tools/SahkkuBench -- --p1 rule --p2 llm --endpoint http://localhost:1234/v1/chat/completions --model pairflow-player
```

---

## Project Structure

- `Sahkku/Assets/Scripts/Rules/`: Pure-C# game rules engine and state representation.
- `Sahkku/Assets/Resources/SahkkuRules.json`: Data-driven ruleset specification.
- `Sahkku/Assets/Scripts/RulesBridge/`: Modular agent interfaces (`IPlayerAgent`), agent implementations (Human, Random, Rule-Based, LLM), and shared config loader (`LlmConfigFile`).
- `Sahkku/Assets/Localization/`: Menu UI, options dialogs, and multi-language string tables.
- `Tools/RulesTests/`: Headless NUnit test suite for the rules engine.
- `Tools/SahkkuBench/`: Headless CLI evaluation and benchmarking harness.
- `Docs/`: Engine specification (`Docs/rules-engine.md`) and historical alignment notes (`Docs/rules-alignment.md`).

---

## Development Notes

- **Tech Stack**: Unity 6 (6.0000.6.0f1) for the graphical client; .NET 8 for headless tools and tests.
- **PC Builds**: When building PC binaries from Unity, manually copy the rules PDF files to the root of the build directory.
- **Dice Rerolling**: In the graphical client, dice are rerolled one at a time because dice are re-sorted after each roll.
