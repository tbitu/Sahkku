using System.IO;
using UnityEngine;
using Sahkku.Rules;

/// <summary>
/// Supplies the shipped ruleset JSON to the engine tests. Kept in its own file because the same
/// test sources are also executed outside Unity, where a host-specific loader is substituted.
/// </summary>
public static class TestRuleset
{
    public static string Json()
    {
        string path = Path.Combine(Application.dataPath, "Resources", "SahkkuRules.json");
        return File.ReadAllText(path);
    }

    public static RuleSet Load()
    {
        return RuleSetJson.FromJson(Json());
    }
}
