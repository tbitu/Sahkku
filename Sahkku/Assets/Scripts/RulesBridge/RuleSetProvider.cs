using UnityEngine;
using Sahkku.Rules;

/// <summary>Loads the JSON ruleset (Assets/Resources/SahkkuRules.json) that drives the shared rules engine.</summary>
public static class RuleSetProvider
{
    const string ResourcePath = "SahkkuRules";

    static RuleSet cached;

    public static RuleSet Load()
    {
        if (cached == null)
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                throw new RuleSetException("Missing ruleset resource '" + ResourcePath + "' (expected Assets/Resources/" + ResourcePath + ".json).");
            }
            cached = RuleSetJson.FromJson(asset.text);
        }
        return cached;
    }
}
