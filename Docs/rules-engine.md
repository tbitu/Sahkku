# Sáhkku rules engine

The rules of Sáhkku are not hard-coded in a `MonoBehaviour`. They live in a JSON ruleset that is
interpreted by a pure-C# engine which is shared by the Unity game and by NPC/LLM tooling.

The ruleset is the single source of truth: every rule decision the engine makes is selected by a value
in `Assets/Resources/SahkkuRules.json`. There is a test (`NoSchemaFieldIsLeftUnread`) that fails when a
key is added to the JSON that the engine never consults, so a rule can never be written and silently
ignored.

## Layers

| Layer | Assembly | Unity? | Responsibility |
| --- | --- | --- | --- |
| Rules engine | `Sahkku.Rules` (`Assets/Scripts/Rules/`) | no (`noEngineReferences`) | Board/domain types, the track, JSON ruleset parsing, all rule decisions |
| Unity bridge | `Sahkku.RulesBridge` (`Assets/Scripts/RulesBridge/`) | partly | Loads the ruleset and provides `UnityEngine.Random` sources. `PlayerAgents.cs` (human/random/heuristic agents) is engine-free, so it also compiles headlessly and is covered by `PlayerAgentTests` |
| Match controller | `Assembly-CSharp` (`Assets/Scripts/GameLogic.cs`) | yes | Owns the match state machine: rolls, asks the active `IPlayerAgent` for the re-roll and the move, and falls back to the heuristic agent when an agent fails. Decides no rule itself |
| Presentation | `Assembly-CSharp` (`Assets/Scripts/GameInteraction.cs`) | yes | Board/dice rendering, turn banner, input; answers the human half of `IHumanInteraction` |

The engine only depends on `System.*`, so the same rule code runs inside Unity, in EditMode tests and
on a server that drives an LLM NPC.

### Engine API (`Sahkku.Rules.RulesEngine`)

```csharp
GameState InitGame(EngineOptions options);               // options: startingPlayer, evenOdds
PieceOwner ThrowForStartingPlayer(IRandomSource random); // "first to roll X starts" (see start.mode)
void      RollAllDice(GameState state, IRandomSource random);
void      RollAndBeginTurn(GameState state, IRandomSource random); // roll + order + open the move phase
void      OrderDice(GameState state);                    // the ruleset's spending order
void      RerollFirstDie(GameState state, IRandomSource random);
bool      CanReroll(GameState state);
bool      EvaluateAllowedPlaces(GameState state);        // fills Piece.allowedPlaces; true if any move exists
List<Move> GetLegalMoves(GameState state);               // the cached view (call EvaluateAllowedPlaces first)
List<Move> LegalMoves(GameState state);                  // the same list, computed from scratch
bool      IsLegalMove(GameState state, Move move);       // pure legality check
List<int> GetAllowedPlaces(GameState state, Piece piece); // board cells
List<int> GetAllowedArcs(GameState state, Piece piece);   // the authoritative form (track arcs)
List<RuleEvent> ApplyMove(GameState state, Move move);   // throws IllegalMoveException on an illegal move
bool      TryApplyMove(GameState state, Move move, out List<RuleEvent> events);
void      NextPlayerTurn(GameState state);
List<string> ValidateState(GameState state);             // invariants; an empty list means sound

// track helpers (for hosts, tooling and tests)
int TrackLength; int PlaceOfArc(PieceOwner owner, int arc); int ArcOfPlace(PieceOwner owner, int place);
int HomeRowOf(PieceOwner owner);
```

`Piece`/`Place`/`GameState`/`Move`/`RuleEvent` and the enums (`PieceType`, `PieceOwner`, `DieFace`,
`TurnPhase`, `WinReason`) live in `Assets/Scripts/Rules/Domain.cs`; the track lives in
`Assets/Scripts/Rules/Track.cs`.

`ApplyMove` **refuses illegal moves** (`IllegalMoveException`) so that no caller — UI, replay file,
network message or LLM — can drive the game into an illegal position. `TryApplyMove` is the
non-throwing form, and `GameState.Clone()` lets a search (or a test) try a move without touching the
live game.

`ApplyMove` returns `RuleEvent`s (`PieceMoved`, `SoldierCaptured`, `KingRecruited`, `QueenCaptured`,
`GameWon` in the order the original code played its sounds); `GameLogic.PlayRuleEvents` maps them to
`AudioManager` calls. When the game ends the engine also sets `GameState.winner` and
`GameState.winReason`, so the host never has to re-derive *why* it ended. The engine itself never
touches audio, visuals or `UnityEngine`.

## The track (why pieces carry an `arc`)

Soldiers, the king and the queen walk an "8"-shaped track: **right along your home row, left along the
middle row, right along the enemy row, left along the middle row again, and then down into your home row
to start over.** The enemy row is visited once per lap but the middle row is visited twice, so a lap is
`4 × board.width` cells long while the board only has `3 × board.width`.

Two consequences shape the engine:

* A piece's position cannot be recovered from its board cell: the same middle-row cell is reached on the
  way out and on the way back, and what happens at the end of that row differs. Every piece therefore
  stores the **arc** it stands on (`Piece.arc`), and `Piece.placeIndex` is the board cell that arc points
  at. A move is *"advance N arcs"* and the board cell follows.
* Bends between rows are part of the same straight move. Stepping from the end of the home row onto the
  middle row counts as going forwards, which is what lets the king "go around a bend" without turning.

`track.legs` describes one lap for player 1 in board coordinates; the second player's lap is the board
mirror of it, because the two players sit at opposite ends of the same board.

## The JSON ruleset

The ruleset is authored by hand at `Assets/Resources/SahkkuRules.json` and loaded by name through
`RuleSetProvider`. It is parsed by the engine's own dependency-free reader (`Sahkku.Rules.JsonParser`),
so no third-party serializer is required.

Top-level sections:

| Key | Meaning |
| --- | --- |
| `board` | `width`, `height`, `layout` (`rowMajorSnake` = row 0 left→right, row 1 right→left, row 2 left→right) |
| `track` | `legs`: the cells of one lap, in order, as `{row, direction}` pairs (`direction` is `right`/`left`) |
| `pieces` | `soldier`, `queen`, `king` rule primitives (see below) |
| `dice` | die count, the face table, the spending order and the re-roll rule |
| `activation` | how activating a piece unlocks the next one in the queue |
| `inactive` | what may happen to pieces that have not been activated yet |
| `start` | how the starting player is decided |
| `setup` | starting cells for soldiers, queens and the neutral king |
| `variants` | `standard` and `evenOdds` starting positions |
| `win` | winning conditions |

Per-piece primitives:

| Key | Meaning |
| --- | --- |
| `moves` | `forward`, `backward` (along the track) and `vertical` (same x, y ± the die value) |
| `movesScaleWithDie` | the step count comes from the current die (otherwise a single step) |
| `startActivatable` | can be activated at the start of the game (queens) |
| `queuesNextOnActivation` | activating it unlocks the next piece in the activation queue |
| `blocksOwnLanding` | other pieces of the mover's side may not land on it |
| `cannotLandOnOwnUnits` | it may not land on a piece of the mover's side |
| `capturable` | landing on it removes it and scores a capture |
| `recruitedWhenLanded` | landing on it changes its owner instead of removing it (king) |
| `landingEndsGame` | landing on it ends the game in the mover's favour (queen) |
| `recruitsKingOnEnemyHomeRow` | reaching the opponent's home row recruits the neutral king (soldier) |

The remaining sections:

| Key | Meaning |
| --- | --- |
| `dice.faces[].steps` | what each face is worth (`sahhku` 1, `three` 3, `two` 2, `zero` 0) |
| `dice.activateFace` | the face that activates a piece |
| `dice.useOrder` | the order faces must be spent in: X, then III, then II, then blank |
| `dice.reroll.faces` | the faces that may be thrown again (the sáhkku X) |
| `dice.reroll.beforeUsingAnyDie` | re-rolls are only offered before any die of the throw is spent |
| `activation.onMoveInactivePiece.unlockOffset` | home-row lines between the mover's source line and the piece that becomes activatable (-1 = the next soldier in the queue) |
| `inactive.enterable` | false = no piece may be moved onto a line holding an unactivated piece |
| `start.mode` | `firstSahhku` (players throw in turn, the first X starts) or `mostSahhku` |
| `variants.*.soldiersActive` | how many of the leading soldiers start loose; the next one starts activatable |
| `win.opponentSoldiersExhausted` | you win when the opponent has no soldiers left |

`RuleSet.Validate()` (called by `RuleSetJson.FromJson`) rejects malformed rulesets with a descriptive
`RuleSetException`: unknown face ids, a spending order that skips a face, a track whose legs do not join
end-to-end with a bend, overlapping setup cells, a variant that leaves no soldier to activate, and a
ruleset with no way to win.

## Tests

`Assets/Tests/Rules/RulesTests.cs` (NUnit 3) covers board mapping, the track (including the second pass
over the middle row and a full lap), standard/even-odds setup, movement per piece/die, capture, recruit,
king activation, `landingEndsGame`, the ruleset-driven variants of those rules, move validation, dice
ordering, re-roll conditions, turn transitions, JSON validation, state invariants and two seeded
full-game simulations.

The engine sources only need `System.*`, so the same tests also run headlessly, which makes the ruleset
verifiable without Unity:

```bash
# Unity:  Window ▸ General ▸ Test Runner ▸ EditMode
# Headless (needs the .NET SDK):
dotnet test Tools/RulesTests
```

## Deliberate behaviour, pinned by tests

* Dice are spent in `dice.useOrder` (X, III, II, blank), so the sáhkku goes first and a blank face last.
* Only the die currently up for spending can be re-rolled, and only while a sáhkku shows
  (`beforeUsingAnyDie`). With several X's the player presses the button once per die, because the
  ruleset re-sorts the dice after every re-roll.
* `evenOdds` marks the three foremost soldiers *loose in place*: they are activated but have not made
  their activation move.
* Landing on your own active soldier is allowed (soldiers stack) and the mover still counts as moved.
* The `RandomPlayerAgent` CPU keeps the original random draw, including its `Clamp(Range(0, 4), …)`
  bias.
* A soldier reaching the opponent's home row recruits the neutral king and announces it with a
  `KingRecruited` event (the pre-engine code recruited the king silently).

## Rule traceability

Every clause of the player-facing rules maps onto a ruleset field and a test:

| Rule (PDF / Reaidu) | Ruleset | Test |
| --- | --- | --- |
| Three dice X/III/II/blank; X = 1 step, activate, or re-roll | `dice` | `ShippedRuleset_DescribesTheVuonnamarkanGame` |
| Faces must be spent X, then III, then II; you lose the II if the III has no move | `dice.useOrder` | `OrderDice_SpendsThreeBeforeTwoBeforeBlank`, `RollAndBeginTurn_HandsOverWhenNoDieCanBeUsed` |
| When you roll X you may throw those dice again | `dice.reroll` | `Reroll_DoesNotRequireAnAlreadyActivePiece`, `Reroll_IsOnlyOfferedBeforeAnyDieHasBeenSpent` |
| Soldiers are activated in queue order, foremost first; activating moves them one line | `activation`, `queuesNextOnActivation` | `MovingAnInactiveSoldier_MakesTheNeighbourActivatable`, `InactiveSoldier_NeedsSahhkuAndActivation` |
| Unactivated pieces cannot move, be captured, or have their line entered | `inactive.enterable` | `Soldier_CannotLandOnInactiveOpponent`, `InactiveRule_ComesFromTheRuleset` |
| The "8"-shaped track, including the second middle-row pass and the return home | `track.legs` | `Track_WalksTheFigureOfEightAndReturnsToTheHomeRow`, `SecondPassOverTheMiddleRow_DescendsIntoTheHomeRow`, `Soldier_FullLap_ReturnsToItsStartingCell` |
| Both players walk mirrored tracks | the `track` mirror | `Track_IsTheBoardMirrorForTheSecondPlayer` |
| Every piece on a line is captured | `capturable` | `CapturingASoldier_ScoresAndRemovesIt` |
| Royal pieces never share a line, except when recruiting the king | `blocksOwnLanding`, `cannotLandOnOwnUnits`, `recruitedWhenLanded` | `Soldier_CannotLandOnOwnQueenOrKing`, `LandingOnTheKing_RecruitsIt` |
| The king crosses rows only with the exact die value, and goes around bends | `moves` | `King_CrossesRowsOnlyWithTheExactDieValue`, `King_CrossingAtTheBendIsAlsoAStraightStep` |
| The king is recruited when a soldier enters the enemy home row | `recruitsKingOnEnemyHomeRow` | `MovingASoldierIntoEnemyTerritory_ActivatesTheNeutralKing`, `KingRecruitmentComesFromTheRuleset` |
| The queen starts unactivated and may be activated in any direction | `startActivatable` | `Queen_MovesForwardBackwardAndVertically` |
| Losing the queen loses the game; losing every soldier loses the game | `landingEndsGame`, `win` | `CapturingTheQueen_EndsTheGame`, `CapturingEveryEnemySoldier_EndsTheGame`, `LosingTheQueenOnlyEndsTheGameWhenTheRulesetSaysSo` |
| "The first one to get X starts" | `start.mode` | `ThrowForStartingPlayer_FirstSahhkuStarts` |
| "Like odds": three soldiers are taken loose | `variants.evenOdds.soldiersActive` | `InitGame_EvenOdds_MarksTheThreeForemostSoldiersLoose` |

## Agents (`Sahkku.Rules.Bridge`)

`IPlayerAgent` is the seam. A side is played by a `HumanPlayerAgent` (which forwards both decisions to
`IHumanInteraction`, implemented by the Unity presentation), a `RandomPlayerAgent` (the AI the game
shipped with) or a `HeuristicPlayerAgent` (deterministic scoring; also the controller's fallback). An
LLM-backed implementation would:

1. Read the same `SahkkuRules.json` (it is plain, string-keyed JSON, easy to put in a prompt).
2. Call `LegalMoves(state)` (or `EvaluateAllowedPlaces` + `GetLegalMoves`) to obtain the legal actions.
3. Return a `Move`; the controller keeps it only when it is in the legal list and `TryApplyMove` accepts
   it: the model may propose anything, but only legal moves reach the board.
