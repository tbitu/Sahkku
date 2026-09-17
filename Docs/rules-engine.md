# Sáhkku rules engine

The rules of Sáhkku are no longer hard-coded in a `MonoBehaviour`. They live in a JSON ruleset that
is interpreted by a pure-C# engine which is shared by the Unity game and (in future) by LLM-driven
NPC players.

## Layers

| Layer | Assembly | Unity? | Responsibility |
| --- | --- | --- | --- |
| Rules engine | `Sahkku.Rules` (`Assets/Scripts/Rules/`) | no (`noEngineReferences`) | Board/domain types, JSON ruleset parsing, all rule decisions |
| Unity bridge | `Assembly-CSharp` (`Assets/Scripts/RulesBridge/`) | yes | Loads the ruleset, provides `UnityEngine.Random` die rolls and the CPU policy |
| Game driver | `Assembly-CSharp` (`Assets/Scripts/GameLogic.cs`, `GameInteraction.cs`) | yes | Input, timers, animation, audio; delegates every rule decision to the engine |

The engine only depends on `System.*`, so the same rule code runs inside Unity, in EditMode tests and
on a server that drives an LLM NPC.

### Engine API (`Sahkku.Rules.RulesEngine`)

```csharp
GameState InitGame(EngineOptions options);            // options: startingPlayer, evenOdds
void      RollAllDice(GameState state, IRandomSource random);
void      OrderDice(GameState state);                 // ascending face value, as before
void      RerollFirstDie(GameState state, IRandomSource random);
bool      CanReroll(GameState state);
bool      EvaluateAllowedPlaces(GameState state);     // fills Piece.allowedPlaces; true if any move exists
List<Move> GetLegalMoves(GameState state);            // flat action list (the LLM-facing view)
List<RuleEvent> ApplyMove(GameState state, Move move); // returns presentation events, advances the turn
void      NextPlayerTurn(GameState state);
```

`Piece`/`Place`/`GameState`/`Move`/`RuleEvent` and the enums (`PieceType`, `PieceOwner`,
`DieFace`, `TurnPhase`) live in `Assets/Scripts/Rules/Domain.cs`.

`ApplyMove` returns `RuleEvent`s (`PieceMoved`, `SoldierCaptured`, `KingRecruited`, `QueenCaptured`,
`GameWon`) in the order the original code played its sounds; `GameLogic.PlayRuleEvents` maps them to
`AudioManager` calls. The engine itself never touches audio, visuals or `UnityEngine`.

## The JSON ruleset

The ruleset is authored by hand at `Assets/Resources/SahkkuRules.json` and loaded by name through
`RuleSetProvider`. It is parsed by the engine's own dependency-free reader (`Sahkku.Rules.JsonParser`),
so no third-party serializer is required.

Top-level sections:

| Key | Meaning |
| --- | --- |
| `board` | `width`, `height`, `layout` (`rowMajorSnake` = row 0 left→right, row 1 right→left, row 2 left→right) |
| `pieces` | `soldier`, `queen`, `king` rule primitives (see below) |
| `dice` | die count, face table (`sahhku`=1, `three`=3, `two`=2, `zero`=0), activation/reroll faces |
| `activation` | offset that makes the neighbouring soldier activatable when an inactive soldier moves |
| `setup` | starting rows/positions for soldiers, queens and the neutral king |
| `variants` | `standard` and `evenOdds` starting activation lists |
| `win` | `soldierCapturesToWin` |

Per-piece primitives:

| Key | Meaning |
| --- | --- |
| `moves` | `forward`, `backward`, `vertical` (same x, y ± die value) |
| `movesScaleWithDie` | step count comes from the current die |
| `startActivatable` | can be activated at the start of the game (queens) |
| `blocksOwnLanding` | other pieces of the mover's side may not land on it |
| `cannotLandOnOwnUnits` | it may not land on a piece of the mover's side |
| `capturable` | landing on it removes it and scores a capture |
| `recruitedWhenLanded` | landing on it changes its owner instead of removing it (king) |
| `landingEndsGame` | landing on it ends the game in the mover's favour (queen) |
| `recruitedOnEnemyTerritory` | recruited by a soldier reaching the enemy home territory (king) |

`RuleSet.Validate()` (called by `RuleSetJson.FromJson`) rejects malformed rulesets with a descriptive
`RuleSetException`.

## Deliberately preserved behaviour

The extraction is a pure refactor: gameplay is byte-for-byte identical to the pre-refactor build,
including these known quirks. Any change to them is a separate, intentional decision.

- Dice are ordered ascending by face value, so `sahhku` (1) is the first die and `zero` the last.
- Re-rolls only ever affect the first die (`rerollRequiresFirstDie`).
- The `evenOdds` starting positions are kept exactly as they were, including the three forward
  soldiers that the readme lists as incorrectly placed.
- Landing on your own active soldier is allowed (pieces stack); the mover still counts as moved.
- The king activation reaches `pieces[0]` of the king's place.

## Future: LLM-driven NPCs

`IActionSelector` is the seam. Today `GameLogic` uses `RandomActionSelector`, which reproduces the
original AI draws exactly. An LLM-backed implementation would:

1. Read the same `SahkkuRules.json` (it is plain, string-keyed JSON, easy to put in a prompt).
2. Call `EvaluateAllowedPlaces` + `GetLegalMoves` to obtain the legal actions and feed them to the
   model.
3. Validate the model's chosen `Move` against the engine and let `ApplyMove` apply it.

Because the engine is Unity-free, that NPC can run in a tool or service that never loads Unity.

## Tests

`Assets/Tests/Rules/RulesTests.cs` (EditMode, NUnit) locks in the behaviour above: board mapping,
standard/even-odds setup, movement per piece/die, capture/recruit/king-activation, win conditions,
dice ordering, re-roll conditions, turn transitions, JSON validation and a seeded full-game
consistency simulation.

Run them from Unity via **Window ▸ General ▸ Test Runner ▸ EditMode**. The same engine sources also
compile and run outside Unity (they only need `System.*`).
