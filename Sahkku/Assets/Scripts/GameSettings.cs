using UnityEngine;

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

        /// <summary>Reserved for the LLM NPC (task 3); the heuristic bot plays this side until then.</summary>
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
