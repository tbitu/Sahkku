Play: https://zhamul.itch.io/digital-shkku

Sáhkku is a Sámi board game that has existed for hundreds of years.

In this repository is a digital version of the game.

Notes:
 - Made with Unity 6.0000.6.0f1
 - When building for PC, manually copy rules pdf files to root of the build
 - The rules live in a JSON ruleset (Sahkku/Assets/Resources/SahkkuRules.json) interpreted by the
   pure-C# engine in Sahkku/Assets/Scripts/Rules/. See Docs/rules-engine.md for the schema and
   Docs/rules-alignment.md for how the ruleset maps onto the printed rules.
 - The ruleset is verified by tests that also run without Unity: `dotnet test Tools/RulesTests`.

Known issues:
 - You can only reroll one sáhkku at a time (press button multiple times to reroll each die as
   workaround). This is by design: the dice are re-sorted after every re-roll.
 - The menu picks the starting player, while the printed rules say the players throw for it. The rule
   is implemented in the engine (`RulesEngine.ThrowForStartingPlayer`) but the menu does not use it.
 - "Like odds" marks the three foremost soldiers loose in place; see Docs/rules-alignment.md for why
   that reading was chosen.
