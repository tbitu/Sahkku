using System;
using System.IO;
using Sahkku.Rules;

/// <summary>
/// Supplies the shipped ruleset JSON to the engine tests.
///
/// Deliberately free of Unity references so the very same test sources can run in the Unity
/// EditMode test runner *and* headlessly through Tools/RulesTests (<c>dotnet test</c>).
/// </summary>
public static class TestRuleset
{
    const string RelativePath = "Sahkku/Assets/Resources/SahkkuRules.json";
    const string EnvironmentVariable = "SAHKKU_RULESET";

    public static string Path()
    {
        string fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment)) return fromEnvironment;

        // Search upwards from wherever the test host happens to run, so the same code works from
        // `dotnet test`, from the Unity EditMode runner and from an IDE runner.
        string found = Search(AppContext.BaseDirectory) ?? Search(Environment.CurrentDirectory);
        if (found != null) return found;

        throw new FileNotFoundException(
            "Could not locate '" + RelativePath + "'. Set " + EnvironmentVariable + " to the ruleset file path.");
    }

    static string Search(string start)
    {
        if (string.IsNullOrEmpty(start)) return null;
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            string candidate = System.IO.Path.Combine(directory.FullName, RelativePath);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return null;
    }

    public static string Json()
    {
        return File.ReadAllText(Path());
    }

    public static RuleSet Load()
    {
        return RuleSetJson.FromJson(Json());
    }
}
