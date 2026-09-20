using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sahkku.Rules;

namespace Sahkku.Rules.Bridge
{
    /// <summary>
    /// The LLM NPC: it asks an OpenAI-compatible endpoint to pick one of the moves the engine has already
    /// declared legal, and accepts the answer only after validating it against that list. The rules engine
    /// stays the sole authority — this agent can only ever return a member of <c>legalMoves</c>, never a
    /// mutation of the state.
    ///
    /// Every failure resolves the same way, without throwing and without hanging the match: no transport,
    /// connection refused, HTTP non-200, timeout, an answer that is not the JSON it was asked for, an
    /// index outside the legal list, or a re-roll that is not on offer. All of them log once and fall back
    /// to the deterministic <see cref="HeuristicPlayerAgent"/>. A <see cref="OperationCanceledException"/>
    /// for the *caller's* token is the one exception, because that is the match ending rather than the
    /// model failing.
    /// </summary>
    public sealed class LlmPlayerAgent : IPlayerAgent
    {
        /// <summary>Role prompt for a move decision. The JSON shape is repeated in the user turn next to the board.</summary>
        public const string MoveSystemPrompt =
            "You are a Sáhkku player. You will be given the board, the dice in spending order and the " +
            "numbered list of legal moves for the current player.\n" +
            "Choose the strongest move and answer with one JSON object and nothing else.";

        /// <summary>Role prompt for the optional re-roll decision.</summary>
        public const string RerollSystemPrompt =
            "You are a Sáhkku player. You may throw the sáhkku die again, or keep the dice and play on.\n" +
            "Decide and answer with one JSON object and nothing else.";

        /// <summary>Appended to the formatted state for a move decision (the contract the parser enforces).</summary>
        public const string MoveInstruction =
            "\nChoose one move. Answer ONLY with JSON: {\"move_index\": <number>, \"reasoning\": \"<short reason>\"}";

        /// <summary>Appended to the formatted state for a re-roll decision (the contract the parser enforces).</summary>
        public const string RerollInstruction =
            "\nShould the sáhkku die be thrown again? Answer ONLY with JSON: {\"reroll\": <true|false>, \"reasoning\": \"<short reason>\"}";

        readonly ILlmTransport transport;
        readonly LlmConfig config;
        readonly RulesEngine engine;
        readonly Action<string> log;

        /// <param name="engine">The authority the fallback and the re-roll question consult. May be null only for tests.</param>
        /// <param name="log">Optional sink for the agent's decisions and fallbacks (Unity passes <c>Debug.Log</c>).</param>
        public LlmPlayerAgent(
            PieceOwner owner,
            string name,
            ILlmTransport transport,
            LlmConfig config,
            RulesEngine engine,
            Action<string> log = null)
        {
            Owner = owner;
            Name = name ?? "LLM NPC";
            this.transport = transport;
            this.config = config ?? new LlmConfig();
            this.engine = engine;
            this.log = log;
        }

        public PieceOwner Owner { get; private set; }

        public string Name { get; private set; }

        public async Task<RerollDecision> DecideRerollAsync(GameState state, CancellationToken cancellationToken)
        {
            // The engine decides whether a re-roll is even on offer; when it is not, there is nothing to ask.
            if (engine != null && state != null && !engine.CanReroll(state))
                return RerollDecision.KeepDiceAndProceed;

            if (transport == null)
            {
                Log("no endpoint is configured; deciding heuristically.");
                return HeuristicPlayerAgent.ChooseReroll(engine, state);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string prompt = BuildRerollPrompt(state);
                string requestJson = LlmChatRequest.Build(config, RerollSystemPrompt, prompt);
                string responseJson = await transport
                    .PostChatCompletionAsync(config.EndpointUrl, requestJson, cancellationToken)
                    .ConfigureAwait(false);

                bool reroll;
                if (LlmResponseParser.TryParseReroll(responseJson, out reroll))
                {
                    Log("chose to " + (reroll ? "throw the sáhkku die again" : "keep the dice") + ReasoningSuffix(responseJson));
                    return reroll ? RerollDecision.RerollActiveDie : RerollDecision.KeepDiceAndProceed;
                }

                Log("the endpoint did not answer with a usable reroll decision; deciding heuristically.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The match ended while the model was thinking. That is the controller's contract, not a
                // model failure, so it propagates instead of being absorbed by the fallback below.
                throw;
            }
            catch (Exception exception)
            {
                // A timeout lands here: the transport cancels its own deadline token, not the caller's.
                Log("the reroll request failed (" + exception.Message + "); deciding heuristically.");
            }

            return HeuristicPlayerAgent.ChooseReroll(engine, state);
        }

        public async Task<Move> DecideMoveAsync(GameState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken)
        {
            if (legalMoves == null || legalMoves.Count == 0) return default(Move);

            // A forced move is not a decision: the engine left exactly one option, so skip the round trip.
            if (legalMoves.Count == 1) return legalMoves[0];

            if (transport == null)
            {
                Log("no endpoint is configured; playing the heuristic choice.");
                return HeuristicPlayerAgent.ChooseMove(engine, state, legalMoves);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string prompt = GameStateFormatter.FormatPromptContext(state, legalMoves) + MoveInstruction;
                string requestJson = LlmChatRequest.Build(config, MoveSystemPrompt, prompt);
                string responseJson = await transport
                    .PostChatCompletionAsync(config.EndpointUrl, requestJson, cancellationToken)
                    .ConfigureAwait(false);

                int moveIndex;
                if (LlmResponseParser.TryParseMoveIndex(responseJson, legalMoves.Count, out moveIndex))
                {
                    // The index was checked against the count of the list the engine just produced, so the
                    // move is legal by construction: no proposal the model makes can bypass the engine.
                    Move move = legalMoves[moveIndex];
                    Log("chose move " + moveIndex + " of " + legalMoves.Count + " (" + move + ")" + ReasoningSuffix(responseJson));
                    return move;
                }

                Log("the endpoint did not answer with a usable move index; playing the heuristic choice.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log("the move request failed (" + exception.Message + "); playing the heuristic choice.");
            }

            return HeuristicPlayerAgent.ChooseMove(engine, state, legalMoves);
        }

        /// <summary>
        /// The re-roll question, projected from the authoritative state: the board and the dice as the
        /// shared formatter renders them, plus the moves the throw would offer if the dice were kept —
        /// which is exactly what the decision turns on.
        /// </summary>
        string BuildRerollPrompt(GameState state)
        {
            List<Move> movesIfKept = engine == null || state == null ? new List<Move>() : engine.LegalMoves(state);
            return GameStateFormatter.FormatPromptContext(state, movesIfKept) + RerollInstruction;
        }

        /// <summary>The model's own reason, appended to the decision log when it sent one.</summary>
        string ReasoningSuffix(string responseJson)
        {
            if (log == null) return string.Empty;
            string reasoning = LlmResponseParser.ReadReasoning(responseJson);
            return string.IsNullOrEmpty(reasoning) ? string.Empty : ": " + reasoning;
        }

        void Log(string message)
        {
            if (log != null) log(Name + ": " + message);
        }
    }
}
