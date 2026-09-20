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
    /// is LM Studio's local server (start it, load a model, then pick the LLM opponent in the menu).
    /// </summary>
    public static string llmEndpointUrl = "http://localhost:1234/v1/chat/completions";

    /// <summary>The model name the endpoint should load/serve; LM Studio ignores it when one model is loaded.</summary>
    public static string llmModelName = "pairflow-player";

    /// <summary>
    /// How long the endpoint may take before the agent gives up and plays the heuristic choice. Deliberately
    /// short: a bot that stalls the match is worse than the deterministic fallback.
    /// </summary>
    public static float llmTimeoutSeconds = 5.0f;

    /// <summary>The endpoint configuration for a side played by <see cref="AgentType.LlmBot"/>.</summary>
    public static LlmConfig GetLlmConfig()
    {
        return new LlmConfig
        {
            EndpointUrl = llmEndpointUrl,
            ModelName = llmModelName,
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
