Play: https://zhamul.itch.io/digital-shkku

Sáhkku is a Sámi board game that has existed for hundreds of years.

In this repository is a digital version of the game.

Notes:
 - Made with Unity 6.0000.6.0f1
 - When building for PC, manually copy rules pdf files to root of the build
 - The rules live in a JSON ruleset (Sahkku/Assets/Resources/SahkkuRules.json) interpreted by the
   pure-C# engine in Sahkku/Assets/Scripts/Rules/. See Docs/rules-engine.md.
 
Known issues:
 - When soldiers reach the end of the board, they do not loop around
 - In even odd rules the 3 first soldier start from incorrect positions
 - You can only reroll one sáhkku at a time (press button multiple times to reroll each die as workaround)
 
