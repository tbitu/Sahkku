using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Sahkku.Rules;
using Sahkku.Rules.Bridge;

/// <summary>
/// Unity-facing match controller for sáhkku. It owns the match state machine and asks the two player
/// agents (<see cref="IPlayerAgent"/>, configured in <see cref="GameSettings"/>) for every decision;
/// <see cref="RulesEngine"/> remains the only authority on what is legal. This class only paces the
/// match, animates it and turns engine events into sounds — it never decides a rule itself.
/// </summary>
public class GameLogic : MonoBehaviour
{
    public static GameLogic Instance;

    [Header("Pacing")]
    [SerializeField] float diceSettleSeconds = 1.1f;
    [SerializeField] float botThinkSeconds = 1.0f;

    [Header("Safety")]
    [SerializeField] float agentReplyTimeoutSeconds = 10.0f;
    [SerializeField] int maxRerollsPerThrow = 32;

    GameState state;
    RulesEngine engine;
    IRandomSource randomSource;
    IBotRandomSource botRandom;
    readonly IPlayerAgent[] agents = new IPlayerAgent[2];

    /// <summary>
    /// Resources the match's agents own (the LLM transport's HTTP client). Released when the match loop
    /// ends, which is the only place that knows the agents will not be asked for another decision.
    /// </summary>
    readonly List<IDisposable> agentResources = new List<IDisposable>();

    CancellationTokenSource matchCancellation;
    bool matchRunning;

    public static int BOARD_SIZE_X { get { return RuleSetProvider.Load().board.width; } }
    public static int BOARD_SIZE_Y { get { return RuleSetProvider.Load().board.height; } }

    public List<Place> places { get { return state.places; } }
    public List<DieFace> dice { get { return state.dice; } }
    public TurnPhase turnPhase { get { return state.turnPhase; } set { state.turnPhase = value; } }
    public int currentActiveDie { get { return state.currentActiveDie; } set { state.currentActiveDie = value; } }
    public bool gameOver { get { return state.gameOver; } }
    public GameSettings.Player winner { get { return state.winner == PieceOwner.P2 ? GameSettings.Player.Two : GameSettings.Player.One; } }
    public WinReason winReason { get { return state.winReason; } }
    public int p1captures { get { return state.p1Captures; } }
    public int p2captures { get { return state.p2Captures; } }

    /// <summary>The player whose turn it is and who therefore owns the next decision.</summary>
    public PieceOwner GetCurrentPlayer()
    {
        return state.CurrentPlayer;
    }

    /// <summary>True while the current player may still throw the sáhkku die again.</summary>
    public bool CanReroll()
    {
        return engine.CanReroll(state);
    }

    /// <summary>True when a human, rather than a bot, is deciding right now. Drives the turn banner.</summary>
    public bool IsCurrentPlayerHuman
    {
        get { return GameSettings.IsHumanAgent(CurrentAgentType()); }
    }

    void Awake()
    {
        Instance = this;
        // The state has to exist before any Start() runs: GameInteraction builds the board from it.
        InitGame();
    }

    void Start()
    {
        BuildAgents();
        StartMatch();
    }

    void OnDestroy()
    {
        // Cancel only: the match loop's own exit path (see StartMatch) owns the disposal, so no token is
        // ever used after its source has been disposed.
        CancellationTokenSource cancellation = matchCancellation;
        if (cancellation != null) cancellation.Cancel();
    }

    /// <summary>Creates the engine and a fresh state from the ruleset and the settings chosen in the menu.</summary>
    public void InitGame()
    {
        engine = new RulesEngine(RuleSetProvider.Load());
        randomSource = new UnityRandomSource();
        botRandom = new UnityBotRandomSource();

        bool throwForStart = GameSettings.throwForStartingPlayer;
        state = engine.InitGame(
            new EngineOptions(
                GameSettings.startingPlayer == GameSettings.Player.One ? PieceOwner.P1 : PieceOwner.P2,
                GameSettings.evenOdds,
                throwForStart),
            throwForStart ? randomSource : null);
    }

    /// <summary>
    /// The scene's roll button calls this. Throwing the dice happens automatically when a turn starts,
    /// so the button is only meaningful while a human is being asked about a sáhkku re-roll: it then
    /// means "throw that die again".
    /// </summary>
    public void InitDiceRoll()
    {
        if (GameInteraction.Instance != null)
        {
            GameInteraction.Instance.ChooseReroll(RerollDecision.RerollActiveDie);
        }
    }

    void BuildAgents()
    {
        agents[0] = CreateAgent(PieceOwner.P1, GameSettings.p1AgentType, "Player 1");
        agents[1] = CreateAgent(PieceOwner.P2, GameSettings.p2AgentType, "Player 2");
    }

    IPlayerAgent CreateAgent(PieceOwner owner, GameSettings.AgentType type, string name)
    {
        switch (type)
        {
            case GameSettings.AgentType.Human:
                return new HumanPlayerAgent(owner, name, GameInteraction.Instance);
            case GameSettings.AgentType.RandomBot:
                return new RandomPlayerAgent(owner, name, botRandom);
            case GameSettings.AgentType.LlmBot:
                return CreateLlmAgent(owner, name);
            default:
                if (type != GameSettings.AgentType.HeuristicBot)
                {
                    Debug.LogWarning("Agent type " + type + " is not implemented; using the heuristic agent.", this);
                }
                return new HeuristicPlayerAgent(owner, name, engine);
        }
    }

    /// <summary>
    /// Wires the LLM NPC: the endpoint configuration comes from <see cref="GameSettings"/>, the transport
    /// is this match's own (and therefore disposed with it), and every decision the model cannot answer is
    /// logged and played heuristically instead — see <see cref="LlmPlayerAgent"/>.
    ///
    /// Even building the transport can fail on a platform without a usable socket stack, and a bot that
    /// cannot be created must not be able to take the match down with it: that seat plays the heuristic
    /// policy instead. The transport joins the match's resource list only once the agent it belongs to
    /// exists, so a failed creation leaves nothing behind.
    /// </summary>
    IPlayerAgent CreateLlmAgent(PieceOwner owner, string name)
    {
        try
        {
            LlmConfig config = GameSettings.GetLlmConfig();
            var transport = new HttpClientLlmTransport(null, config.RequestTimeout);
            var agent = new LlmPlayerAgent(owner, name, transport, config, engine, message => Debug.Log("[LLM] " + message, this));
            agentResources.Add(transport);
            return agent;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("The LLM NPC for " + name + " could not be created (" + exception.Message + "); the heuristic agent plays that seat instead.", this);
            return new HeuristicPlayerAgent(owner, name, engine);
        }
    }

    GameSettings.AgentType CurrentAgentType()
    {
        return state.CurrentPlayer == PieceOwner.P1 ? GameSettings.p1AgentType : GameSettings.p2AgentType;
    }

    IPlayerAgent CurrentAgent()
    {
        return state.CurrentPlayer == PieceOwner.P1 ? agents[0] : agents[1];
    }

    // ------------------------------------------------------------------ the match loop

    /// <summary>
    /// Runs one whole match. Every decision is an <c>await</c> on an agent, so a bot never blocks the
    /// frame and a human never stalls the match for anybody else.
    /// </summary>
    async void StartMatch()
    {
        if (matchRunning) return;
        matchRunning = true;

        matchCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = matchCancellation.Token;

        try
        {
            await WaitForPresentationAsync(cancellationToken);

            while (!state.gameOver && !cancellationToken.IsCancellationRequested)
            {
                if (state.IsRollPhase)
                {
                    engine.RollDice(state, randomSource);
                    AnimateDiceThrow();
                    GameInteraction.Instance.UpdatePieces();
                    await DelayAsync(diceSettleSeconds, cancellationToken);
                }

                if (state.gameOver) break;

                await ResolveRerollDecisionAsync(CurrentAgent(), cancellationToken);
                if (state.gameOver) break;

                await PlayMovePhaseAsync(CurrentAgent(), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The scene left (or the bubble was stopped); the match simply stops here.
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
        }
        finally
        {
            matchRunning = false;

            // The agents' own resources die with the match: a transport left alive per scene load would
            // keep its connections (and its handlers) forever.
            foreach (IDisposable resource in agentResources) resource.Dispose();
            agentResources.Clear();

            // Release the match's token source here, and only here: the loop is the last user of its token,
            // so a long session does not keep the old source (and every registration on it) alive.
            CancellationTokenSource finished = matchCancellation;
            matchCancellation = null;
            if (finished != null) finished.Dispose();
        }
    }

    /// <summary>
    /// Resolves the optional sáhkku re-roll. The engine never decides this, so the active agent is asked
    /// until it keeps the dice (or until the safety cap stops a bot that keeps drawing sáhkku).
    /// </summary>
    async Task ResolveRerollDecisionAsync(IPlayerAgent agent, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < maxRerollsPerThrow; ++attempt)
        {
            if (state.gameOver || cancellationToken.IsCancellationRequested) return;

            if (!engine.CanReroll(state))
            {
                // Not on offer: keeping the dice is the only legal resolution, and it opens the move phase.
                engine.ApplyRerollDecision(state, null, RerollDecision.KeepDiceAndProceed);
                return;
            }

            RerollDecision decision = await RequestRerollAsync(agent, cancellationToken);
            if (decision == RerollDecision.RerollActiveDie)
            {
                engine.ApplyRerollDecision(state, randomSource, RerollDecision.RerollActiveDie);
                GameInteraction.Instance.RollDice(state.currentActiveDie);
                AudioManager.Instance.PlayRandomSound("BircutOkta", 8, 0.5f);
                GameInteraction.Instance.UpdatePieces();
                await DelayAsync(diceSettleSeconds, cancellationToken);
                continue;
            }

            engine.ApplyRerollDecision(state, null, RerollDecision.KeepDiceAndProceed);
            return;
        }

        Debug.LogWarning("Re-rolling was capped after " + maxRerollsPerThrow + " attempts; keeping the dice.", this);
        if (!state.gameOver && !state.IsRollPhase) engine.ApplyRerollDecision(state, null, RerollDecision.KeepDiceAndProceed);
    }

    async Task PlayMovePhaseAsync(IPlayerAgent agent, CancellationToken cancellationToken)
    {
        while (!state.gameOver && !state.IsRollPhase && !cancellationToken.IsCancellationRequested)
        {
            List<Move> legalMoves = engine.LegalMoves(state);
            if (legalMoves.Count == 0)
            {
                // No die of this throw can be spent: the engine hands the turn over.
                engine.EvaluateAllowedPlaces(state);
                engine.NextPlayerTurn(state);
                GameInteraction.Instance.UpdatePieces();
                return;
            }

            if (!IsCurrentPlayerHuman) await DelayAsync(botThinkSeconds, cancellationToken);

            Move move = await RequestMoveAsync(agent, legalMoves, cancellationToken);
            ApplyMove(move, legalMoves);
            await DelayAsync(0.15f, cancellationToken);
        }
    }

    /// <summary>
    /// Applies an agent's move. A move the engine refuses can never reach the board, so the controller
    /// substitutes the deterministic heuristic choice instead of hanging the match.
    /// </summary>
    void ApplyMove(Move move, List<Move> legalMoves)
    {
        List<RuleEvent> events;
        if (!engine.TryApplyMove(state, move, out events))
        {
            Debug.LogWarning("Agent " + CurrentAgent().Name + " proposed " + move + ", which is not legal; playing the heuristic choice.", this);
            Move fallback = HeuristicPlayerAgent.ChooseMove(engine, state, legalMoves);
            if (!engine.TryApplyMove(state, fallback, out events))
            {
                // Unreachable: every fallback comes from the list the engine just reported as legal.
                Debug.LogError("No legal move could be applied; handing the turn over.", this);
                engine.NextPlayerTurn(state);
                GameInteraction.Instance.UpdatePieces();
                return;
            }
        }

        PlayRuleEvents(events);
        GameInteraction.Instance.UpdatePieces();
    }

    // ------------------------------------------------------------------ agent queries

    async Task<RerollDecision> RequestRerollAsync(IPlayerAgent agent, CancellationToken cancellationToken)
    {
        try
        {
            Task<RerollDecision> pending = agent.DecideRerollAsync(state, cancellationToken);
            if (pending == null) return RerollDecision.KeepDiceAndProceed;

            if (!IsCurrentPlayerHuman && await TimedOutAsync(pending, cancellationToken))
            {
                Debug.LogWarning("Agent " + agent.Name + " did not answer in time; keeping the dice.", this);
                return RerollDecision.KeepDiceAndProceed;
            }

            RerollDecision decision = await pending;
            if (decision == RerollDecision.RerollActiveDie && !engine.CanReroll(state))
            {
                Debug.LogWarning("Agent " + agent.Name + " asked for a re-roll that is not on offer; keeping the dice.", this);
                return RerollDecision.KeepDiceAndProceed;
            }
            return decision;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The match ended: that is the controller's own contract, not a failure to recover from.
            throw;
        }
        catch (Exception exception)
        {
            // A platform that cannot do the request at all (WebGL has no socket stack) and a deadline the
            // transport cancelled on its own both land here: neither is worth losing the match over, so
            // the deterministic policy answers instead.
            Debug.LogWarning("Agent " + agent.Name + " failed to choose a re-roll (" + exception.Message + "); keeping the dice.", this);
            return HeuristicPlayerAgent.ChooseReroll(engine, state);
        }
    }

    async Task<Move> RequestMoveAsync(IPlayerAgent agent, List<Move> legalMoves, CancellationToken cancellationToken)
    {
        try
        {
            Task<Move> pending = agent.DecideMoveAsync(state, legalMoves, cancellationToken);
            if (pending == null) return HeuristicPlayerAgent.ChooseMove(engine, state, legalMoves);

            if (!IsCurrentPlayerHuman && await TimedOutAsync(pending, cancellationToken))
            {
                Debug.LogWarning("Agent " + agent.Name + " did not answer in time; playing the heuristic choice.", this);
                return HeuristicPlayerAgent.ChooseMove(engine, state, legalMoves);
            }

            Move move = await pending;
            if (!Contains(legalMoves, move))
            {
                Debug.LogWarning("Agent " + agent.Name + " proposed " + move + ", which is not a legal move; playing the heuristic choice.", this);
                return HeuristicPlayerAgent.ChooseMove(engine, state, legalMoves);
            }
            return move;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The match ended: that is the controller's own contract, not a failure to recover from.
            throw;
        }
        catch (Exception exception)
        {
            // As in the re-roll path: a transport that cannot reach its endpoint — on WebGL, one whose
            // socket stack does not exist — costs the match the model's answer, never the turn.
            Debug.LogWarning("Agent " + agent.Name + " failed to choose a move (" + exception.Message + "); playing the heuristic choice.", this);
            return HeuristicPlayerAgent.ChooseMove(engine, state, legalMoves);
        }
    }

    static bool Contains(List<Move> legalMoves, Move move)
    {
        foreach (Move candidate in legalMoves)
        {
            if (candidate.pieceId == move.pieceId && candidate.targetPlaceIndex == move.targetPlaceIndex) return true;
        }
        return false;
    }

    /// <summary>
    /// True when the agent did not answer within <see cref="agentReplyTimeoutSeconds"/> of frame time
    /// (humans are never timed out).
    /// </summary>
    Task<bool> TimedOutAsync(Task pending, CancellationToken cancellationToken)
    {
        // The deadline is polled on the frame clock rather than raced against a timer, so it also holds on
        // a platform where the timer behind Task.Delay never fires — see <see cref="UnityTimeDelays"/>.
        return UnityTimeDelays.TimedOutAsync(pending, agentReplyTimeoutSeconds, cancellationToken);
    }

    async Task WaitForPresentationAsync(CancellationToken cancellationToken)
    {
        // Start() order between this controller and GameInteraction is undefined; the board and the dice
        // have to exist before the first roll animates.
        while (!cancellationToken.IsCancellationRequested &&
               (GameInteraction.Instance == null || !GameInteraction.Instance.IsReady))
        {
            await Task.Yield();
        }
    }

    /// <summary>
    /// Paces the match between animations and bot decisions. The wait itself lives in
    /// <see cref="UnityTimeDelays"/> because it has to be measured in frames: on WebGL the continuation of
    /// a <see cref="Task.Delay"/> is never resumed, and a match loop that stops at its first delay never
    /// reaches the move phase at all.
    /// </summary>
    static Task DelayAsync(float seconds, CancellationToken cancellationToken)
    {
        return UnityTimeDelays.DelayAsync(seconds, cancellationToken);
    }

    // ------------------------------------------------------------------ presentation

    void AnimateDiceThrow()
    {
        for (int i = 0; i < state.dice.Count; ++i)
        {
            GameInteraction.Instance.RollDice(i);
        }
        AudioManager.Instance.PlayRandomSound("BircutGolbma", 13, 0.5f);
    }

    void PlayRuleEvents(List<RuleEvent> events)
    {
        foreach (RuleEvent ruleEvent in events)
        {
            switch (ruleEvent.kind)
            {
                case RuleEventKind.SoldierCaptured:
                    AudioManager.Instance.PlayRandomSound("GodditGalgu", 3);
                    break;

                case RuleEventKind.KingRecruited:
                    AudioManager.Instance.PlaySound("FasketGonagas1", 3);
                    break;

                case RuleEventKind.QueenCaptured:
                    // The win sound is played by the following GameWon event.
                    break;

                case RuleEventKind.GameWon:
                    AudioManager.Instance.PlaySound("Riskut");
                    break;

                case RuleEventKind.PieceMoved:
                    PlayMoveSound(ruleEvent.pieceType, ruleEvent.owner);
                    break;
            }
        }
    }

    void PlayMoveSound(PieceType type, PieceOwner owner)
    {
        if (type == PieceType.Soldier)
        {
            if (owner == PieceOwner.P1) AudioManager.Instance.PlayRandomSound("MuorraGalguOkta", 5);
            else AudioManager.Instance.PlayRandomSound("MuorraOlmmaiOkta", 9);
        }
        else
        {
            if (owner == PieceOwner.P1) AudioManager.Instance.PlayRandomSound("MuorraDronnetSamiOkta", 5);
            else AudioManager.Instance.PlayRandomSound("MuorraDronnetDaccaOkta", 4);
        }
    }
}
