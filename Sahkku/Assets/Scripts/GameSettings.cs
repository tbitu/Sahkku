using System;
using UnityEngine;
using Sahkku.Rules.Bridge;

public class GameSettings : MonoBehaviour
{
    public enum PieceModel
    {
        Wood,
        Bone
    }

    public enum Player
    {
        One,
        Two
    }

    /// <summary>
    /// Who plays a side. The match controller asks the agent registered for
    /// <see cref="GameSettings.AgentType"/> — never "is this player 2 and is it single player?".
    /// </summary>
    public enum AgentType
    {
        Human = 0,
        RandomBot = 1,
        HeuristicBot = 2,

        /// <summary>
        /// The LLM NPC: <see cref="GameSettings.GetLlmConfig"/>'s endpoint picks the move, and every
        /// failure there falls back to <see cref="AgentType.HeuristicBot"/>'s policy.
        /// </summary>
        LlmBot = 3
    }

    public static AgentType p1AgentType = AgentType.Human;
    public static AgentType p2AgentType = AgentType.HeuristicBot;

    public static bool evenOdds = true;

    /// <summary>
    /// When true the starting player is decided by throwing for it (see
    /// <c>RulesEngine.ThrowForStartingPlayer</c>) instead of by <see cref="startingPlayer"/>.
    /// </summary>
    public static bool throwForStartingPlayer = false;

    public static Player startingPlayer = Player.One;
    public static PieceModel p1Model = PieceModel.Wood;
    public static PieceModel p2Model = PieceModel.Wood;
    public static PieceModel kingModel = PieceModel.Wood;
    public static bool muteSounds = false;
    public static bool muteMusic = false;

    /// <summary>
    /// Where the LLM NPC sends its requests: any OpenAI-compatible chat-completions endpoint. The default
    /// is LM Studio's local server (start it, load a model, then pick the LLM opponent in the menu). The
    /// menu edits this and <see cref="llmModelName"/> and persists both through
    /// <see cref="SaveLlmConfig"/>; <see cref="LoadLlmConfig"/> fills them from the shared file at startup.
    /// </summary>
    public static string llmEndpointUrl = LlmConfigFile.DefaultEndpoint;

    /// <summary>The model name the endpoint should load/serve; LM Studio ignores it when one model is loaded.</summary>
    public static string llmModelName = LlmConfigFile.DefaultModel;

    /// <summary>
    /// How long the endpoint may take before the agent gives up and plays the heuristic choice. Deliberately
    /// short: a bot that stalls the match is worse than the deterministic fallback.
    /// </summary>
    public static float llmTimeoutSeconds = 5.0f;

    static bool llmConfigLoaded;

    /// <summary>
    /// Loads the shared <c>llm-config.json</c> once, before the menu or a match reads the endpoint. A
    /// missing or broken file simply leaves the defaults in place — see <see cref="LlmConfigFile"/>, which
    /// never throws on a read. Called by the menu at startup and again, idempotently, by
    /// <see cref="GetLlmConfig"/> so a script that skips the menu still honors the file.
    /// </summary>
    public static void LoadLlmConfig()
    {
        if (llmConfigLoaded) return;
        llmConfigLoaded = true;

        // The player is the one host that is guaranteed a writable directory: hand it to the loader so an
        // edit to a read-only install still has somewhere to go, and is read back from there next launch.
        LlmConfigFile.WritableFallbackDirectory = Application.persistentDataPath;

        (string endpoint, string model) = LlmConfigFile.Load();
        llmEndpointUrl = endpoint;
        llmModelName = model;
    }

    /// <summary>
    /// Persists the current endpoint and model to the shared file. Returns false (and logs) when the file
    /// cannot be written, so a read-only install cannot take the options screen down with it.
    /// </summary>
    public static bool SaveLlmConfig()
    {
        try
        {
            LlmConfigFile.Save(llmEndpointUrl, llmModelName);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not write the shared LLM configuration: " + exception.Message);
            return false;
        }
    }

    /// <summary>The endpoint configuration for a side played by <see cref="AgentType.LlmBot"/>.</summary>
    public static LlmConfig GetLlmConfig()
    {
        LoadLlmConfig();
        return new LlmConfig
        {
            // The menu lets either a base URL or a full chat-completions URL be typed; the client only ever
            // posts to the latter, so the value is completed here rather than at every call site.
            EndpointUrl = LlmConfigFile.NormalizeEndpoint(llmEndpointUrl),
            ModelName = LlmConfigFile.NormalizeModel(llmModelName),
            RequestTimeout = TimeSpan.FromSeconds(Mathf.Max(0.1f, llmTimeoutSeconds))
        };
    }

    public static bool IsHumanAgent(AgentType type)
    {
        return type == AgentType.Human;
    }

    /// <summary>Human against a bot, either way round.</summary>
    public static bool IsSinglePlayer
    {
        get { return IsHumanAgent(p1AgentType) ^ IsHumanAgent(p2AgentType); }
    }

    /// <summary>Two humans sharing one device, passing the turn back and forth.</summary>
    public static bool IsHotseat
    {
        get { return IsHumanAgent(p1AgentType) && IsHumanAgent(p2AgentType); }
    }

    /// <summary>Two bots: useful for benchmarks, not something the menu offers.</summary>
    public static bool IsBotMatch
    {
        get { return !IsHumanAgent(p1AgentType) && !IsHumanAgent(p2AgentType); }
    }
}
