using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Sahkku.Rules;
using Sahkku.Rules.Bridge;

namespace Sahkku.Bench
{
    /// <summary>
    /// The knobs one benchmark run turns.
    ///
    /// The defaults mirror the shipped game — the even-odds setup, player one to move — and add a base
    /// seed, a turn cap and a per-turn deadline: an offline harness has to be replayable, and it has to
    /// end even when an agent misbehaves.
    /// </summary>
    public sealed class MatchConfig
    {
        /// <summary>How many rolls one game may take before it is declared a turn-limit draw.</summary>
        public int MaxHalfMoves = 10000;

        /// <summary>Base seed; each game of a run derives its own stream from <c>Seed + matchIndex</c>.</summary>
        public int Seed = 0;

        /// <summary>When true the seed comes from the clock instead of <see cref="Seed"/>.</summary>
        public bool UseRandomSeed = true;

        /// <summary>Who moves first, unless <see cref="ThrowForStartingPlayer"/> is set.</summary>
        public PieceOwner StartingPlayer = PieceOwner.P1;

        /// <summary>When true the engine throws for the starting player, as the ruleset prescribes.</summary>
        public bool ThrowForStartingPlayer = false;

        /// <summary>Deadline for a single agent decision; zero or a negative value disables it.</summary>
        public TimeSpan TurnTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Plays the ruleset's even-odds variant — the setup the shipped game starts from
        /// (<c>GameSettings.evenOdds</c>), in which both players already have three soldiers loose. With it
        /// off, both sides start fully queued and a game takes far longer to leave its home row.
        /// </summary>
        public bool EvenOdds = true;
    }

    /// <summary>Everything one benchmark game did. The runner owns this record and nothing else writes to it.</summary>
    public sealed class MatchResult
    {
        public int MatchIndex;

        /// <summary>The winner, or <see cref="PieceOwner.None"/> for a draw (turn limit) or a failed game.</summary>
        public PieceOwner Winner;

        public WinReason WinReason;

        /// <summary>Rolls spent in the game, i.e. the turn counter the turn limit measures.</summary>
        public int TotalHalfMoves;

        /// <summary>Throws of the dice; a re-throw of one die counts as a re-roll instead (see <see cref="TotalRerollDecisions"/>).</summary>
        public int TotalDiceRolls;

        /// <summary>Times an agent was asked whether to keep or re-throw the sáhkku die.</summary>
        public int TotalRerollDecisions;

        public int TotalCaptures;

        /// <summary>Wall-clock time the game took, every agent decision included.</summary>
        public TimeSpan Duration;

        /// <summary>Null on a clean game, the reason the game was aborted otherwise.</summary>
        public string ErrorMessage;

        /// <summary>True when an agent proposed an illegal move or a state invariant broke.</summary>
        public bool RuleViolationOccurred;

        /// <summary>True when the game ended by reaching <see cref="MatchConfig.MaxHalfMoves"/>.</summary>
        public bool HitTurnLimit;

        /// <summary>How often an LLM agent gave up on its endpoint and played the heuristic choice.</summary>
        public int LlmFallbacks;

        /// <summary>The final board, so a caller can print it or assert on it. Set once the game has started.</summary>
        public GameState FinalState;

        /// <summary>True when the game did not complete cleanly, whether by rule violation or by an agent/timeout error.</summary>
        public bool Failed { get { return !string.IsNullOrEmpty(ErrorMessage); } }
    }

    /// <summary>
    /// Deterministic randomness for a benchmark run: the engine's die faces and the random bot's draws come
    /// from one small generator, so replaying a seed replays the games exactly. SplitMix64 is written out
    /// rather than borrowed from <see cref="Random"/> so a .NET upgrade cannot silently change a seed's games.
    /// </summary>
    public sealed class SeededRandomSource : IRandomSource, IBotRandomSource
    {
        /// <summary>The number of die faces <see cref="IRandomSource.NextDieFaceIndex"/> promises: an index in [0, 4).</summary>
        const int DieFaceCount = 4;

        ulong state;

        public SeededRandomSource(int seed)
        {
            state = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 0xBF58476D1CE4E5B9UL);
        }

        /// <summary>A die face index in [0, 4), which the engine maps through its ruleset's face table.</summary>
        public int NextDieFaceIndex()
        {
            return (int)(Next() % DieFaceCount);
        }

        /// <summary>A draw in <c>[minimumInclusive, maximumExclusive)</c>; below zero when the range is empty.</summary>
        public int NextInt(int minimumInclusive, int maximumExclusive)
        {
            if (maximumExclusive <= minimumInclusive) return minimumInclusive - 1;
            return minimumInclusive + (int)(Next() % (ulong)(maximumExclusive - minimumInclusive));
        }

        ulong Next()
        {
            state = unchecked(state + 0x9E3779B97F4A7C15UL);
            ulong z = state;
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// Drives one game to its end: roll, ask the active agent about the optional re-roll, ask it for its
    /// move, hand every action to the engine. The runner decides no rule and mutates no state itself — the
    /// engine stays the sole authority and every proposal is checked against it first, so a broken agent can
    /// only ever produce a reported failure, never a corrupted board.
    /// </summary>
    public sealed class MatchRunner
    {
        /// <summary>
        /// How often one throw may be re-thrown before the agent is treated as broken. A re-roll replaces
        /// the die and re-orders the dice, so a fair source ends the loop quickly; the cap is only there to
        /// catch an agent that answers "re-roll" forever.
        /// </summary>
        public const int MaxRerollDecisionsPerThrow = 64;

        readonly RulesEngine engine;
        readonly IPlayerAgent p1Agent;
        readonly IPlayerAgent p2Agent;

        public MatchRunner(RulesEngine engine, IPlayerAgent p1Agent, IPlayerAgent p2Agent)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            if (p1Agent == null) throw new ArgumentNullException("p1Agent");
            if (p2Agent == null) throw new ArgumentNullException("p2Agent");
            if (p1Agent.Owner != PieceOwner.P1)
                throw new ArgumentException("The agent for the first slot must own P1, not " + p1Agent.Owner + ".", "p1Agent");
            if (p2Agent.Owner != PieceOwner.P2)
                throw new ArgumentException("The agent for the second slot must own P2, not " + p2Agent.Owner + ".", "p2Agent");

            this.engine = engine;
            this.p1Agent = p1Agent;
            this.p2Agent = p2Agent;
        }

        /// <summary>
        /// Plays one game. The result always describes what happened: a decisive winner, a turn-limit draw,
        /// or — with <see cref="MatchResult.ErrorMessage"/> set — a game the harness refused to continue
        /// (an illegal proposal, a broken invariant, an agent that timed out or never stopped re-rolling).
        /// A cancellation of <paramref name="cancellationToken"/> is the one thing that propagates, because
        /// that is the caller ending the run rather than a game failing.
        /// </summary>
        public async Task<MatchResult> RunMatchAsync(int matchIndex, MatchConfig config, CancellationToken cancellationToken)
        {
            if (config == null) config = new MatchConfig();

            var result = new MatchResult { MatchIndex = matchIndex };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                IRandomSource random = CreateRandomSource(config, matchIndex);
                var options = new EngineOptions(config.StartingPlayer, config.EvenOdds, config.ThrowForStartingPlayer);
                GameState state = engine.InitGame(options, random);
                result.FinalState = state;

                int halfMoves = 0;
                while (!state.gameOver && halfMoves < config.MaxHalfMoves)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (state.IsRollPhase)
                    {
                        engine.RollDice(state, random);
                        result.TotalDiceRolls++;
                        result.TotalHalfMoves = ++halfMoves;
                    }

                    await ResolveRerollsAsync(state, config, random, result, cancellationToken).ConfigureAwait(false);
                    await PlayMovePhaseAsync(state, config, result, cancellationToken).ConfigureAwait(false);
                }

                if (state.gameOver)
                {
                    result.Winner = state.winner;
                    result.WinReason = state.winReason;
                }
                else
                {
                    // The missing-data rule: a game that runs past the cap is a draw, not a crash, and the
                    // engine's state is left exactly as it was so a caller can still inspect it.
                    result.HitTurnLimit = true;
                    result.Winner = PieceOwner.None;
                    result.WinReason = WinReason.None;
                }

                result.TotalCaptures = state.p1Captures + state.p2Captures;

                List<string> problems = engine.ValidateState(state);
                if (problems.Count > 0)
                    throw new RuleViolationException("the board is not sound at the end of the game: " + Join(problems));
            }
            catch (RuleViolationException exception)
            {
                MarkViolation(result, exception.Message);
            }
            catch (IllegalMoveException exception)
            {
                // Never silenced: an illegal move reaching the engine means an agent bypassed the legality
                // check, which is exactly the failure a benchmark exists to catch.
                MarkViolation(result, exception.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A timeout, a failed decision, or an agent that never converged: the game is aborted with
                // the reason recorded and the runner's own bookkeeping stays intact.
                result.Winner = PieceOwner.None;
                result.WinReason = WinReason.None;
                result.ErrorMessage = exception.GetType().Name + ": " + exception.Message;
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            return result;
        }

        // ------------------------------------------------------------------ the turn loop

        /// <summary>
        /// Asks the active agent about the sáhkku re-roll until the engine stops offering one, and applies
        /// every answer through <see cref="RulesEngine.ApplyRerollDecision"/>. Keeping the dice closes the
        /// question for this throw; re-throwing it can open it again, which is why the loop counts.
        /// </summary>
        async Task ResolveRerollsAsync(GameState state, MatchConfig config, IRandomSource random, MatchResult result, CancellationToken cancellationToken)
        {
            int decisions = 0;
            while (!state.gameOver && engine.CanReroll(state))
            {
                if (++decisions > MaxRerollDecisionsPerThrow)
                    throw new InvalidOperationException("the re-roll decision did not settle after " + MaxRerollDecisionsPerThrow + " rounds.");

                IPlayerAgent agent = AgentFor(state.CurrentPlayer);
                RerollDecision decision = await WithDeadline(
                    config,
                    agent.DecideRerollAsync(state, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                result.TotalRerollDecisions++;

                // The engine validates the decision as well: "re-roll" outside CanReroll is refused, so an
                // agent cannot re-throw a die the ruleset has closed.
                engine.ApplyRerollDecision(state, decision == RerollDecision.RerollActiveDie ? random : null, decision);
            }
        }

        /// <summary>
        /// Plays out the move phase: it asks the current agent for one of the moves the engine declared
        /// legal, checks it, and applies it. When a thrown die leaves nobody able to move, the turn is
        /// handed over — the ruleset forbids skipping a die value, so an unusable throw simply ends the turn.
        /// </summary>
        async Task PlayMovePhaseAsync(GameState state, MatchConfig config, MatchResult result, CancellationToken cancellationToken)
        {
            while (!state.gameOver && !state.IsRollPhase)
            {
                List<Move> legalMoves = engine.LegalMoves(state);
                if (legalMoves.Count == 0)
                {
                    engine.NextPlayerTurn(state);
                    return;
                }

                IPlayerAgent agent = AgentFor(state.CurrentPlayer);
                Move move = await WithDeadline(
                    config,
                    agent.DecideMoveAsync(state, legalMoves, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                // The engine is the authority. A proposal outside the legal list is a critical violation and
                // aborts the game: substituting a different move here would hide the very defect being measured.
                if (!engine.IsLegalMove(state, move))
                    throw new RuleViolationException(
                        agent.Name + " proposed " + move + ", which is not legal (" + legalMoves.Count + " legal moves were offered).");

                engine.ApplyMove(state, move);

                List<string> problems = engine.ValidateState(state);
                if (problems.Count > 0)
                    throw new RuleViolationException("the board is not sound after " + move + ": " + Join(problems));
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Bounds one agent decision by <see cref="MatchConfig.TurnTimeout"/>. The deadline is applied to the
        /// task the agent returned, never by cancelling the shared token, so a slow decision cannot cancel the
        /// rest of the run and is reported as this game's failure instead.
        /// </summary>
        static Task<T> WithDeadline<T>(MatchConfig config, Task<T> decision, CancellationToken cancellationToken)
        {
            if (config.TurnTimeout <= TimeSpan.Zero) return decision;
            return decision.WaitAsync(config.TurnTimeout, cancellationToken);
        }

        IPlayerAgent AgentFor(PieceOwner owner)
        {
            return owner == PieceOwner.P2 ? p2Agent : p1Agent;
        }

        static IRandomSource CreateRandomSource(MatchConfig config, int matchIndex)
        {
            int seed = config.UseRandomSeed
                ? unchecked(Environment.TickCount + matchIndex * 7919)
                : unchecked(config.Seed + matchIndex);
            return new SeededRandomSource(seed);
        }

        static void MarkViolation(MatchResult result, string message)
        {
            result.RuleViolationOccurred = true;
            result.Winner = PieceOwner.None;
            result.WinReason = WinReason.None;
            result.ErrorMessage = "rule violation: " + message;
        }

        static string Join(List<string> problems)
        {
            return string.Join("; ", problems.ToArray());
        }
    }

    /// <summary>
    /// Raised inside the runner when an agent or the engine produced something the harness must not paper
    /// over. It always ends the game with a recorded failure and never escapes <see cref="MatchRunner"/>.
    /// </summary>
    sealed class RuleViolationException : Exception
    {
        public RuleViolationException(string message) : base(message) { }
    }
}
