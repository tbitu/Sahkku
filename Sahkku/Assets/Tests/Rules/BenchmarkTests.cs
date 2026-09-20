// The benchmark harness itself lives in Tools/SahkkuBench, which is outside every Unity assembly
// (Unity only compiles Assets/ and Packages/). The headless runner includes those sources directly
// (see Tools/RulesTests/RulesTests.csproj), so this fixture only exists in the `dotnet test` build;
// in the EditMode runner there is no Sahkku.Bench to test and the file is deliberately empty.
#if !UNITY_5_3_OR_NEWER

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sahkku.Bench;
using Sahkku.Rules;
using Sahkku.Rules.Bridge;

// The headless test project has a SeededRandomSource of its own (engine draws only); this alias keeps the
// benchmark's wider one — it also serves the random bot's piece and move draws — unambiguous here.
using BenchRandomSource = Sahkku.Bench.SeededRandomSource;

namespace Sahkku.Rules.Tests
{
    /// <summary>
    /// The headless match runner: the loop that drives the real engine with real agents, the turn-limit
    /// and failure rules it has to follow, and the statistics it reports.
    ///
    /// Strength is measured against a baseline that is weak by construction. The shipped
    /// <see cref="RandomPlayerAgent"/> re-throws every sáhkku it sees, and this ruleset rewards that: a
    /// sáhkku is worth one step where a two or a three is worth more, and a re-throw is free while no die
    /// of the throw has been spent. Measured over the shipped even-odds setup, Heuristic vs that bot is
    /// 0 wins for the heuristic with ~60% of the games dragging to the turn limit — so it is not a
    /// baseline the heuristic beats, and a test that claimed otherwise would be asserting something false.
    /// </summary>
    [TestFixture]
    public class BenchmarkTests
    {
        static RulesEngine Engine() { return new RulesEngine(TestRuleset.Load()); }

        static MatchConfig Config(int seed, int maxHalfMoves)
        {
            return new MatchConfig
            {
                MaxHalfMoves = maxHalfMoves,
                Seed = seed,
                UseRandomSeed = false
            };
        }

        static MatchRunner Runner(RulesEngine engine, IPlayerAgent p1, IPlayerAgent p2)
        {
            return new MatchRunner(engine, p1, p2);
        }

        // ------------------------------------------------------------------ the match loop

        [Test]
        public void MatchRunner_PlaysDeterministicHeuristicGameToCompletion()
        {
            RulesEngine engine = Engine();
            var p1 = new HeuristicPlayerAgent(PieceOwner.P1, "Heuristic", engine);
            var p2 = new HeuristicPlayerAgent(PieceOwner.P2, "Heuristic", engine);
            MatchConfig config = Config(20260919, 10000);

            MatchResult first = Runner(engine, p1, p2).RunMatchAsync(0, config, CancellationToken.None).Result;
            MatchResult again = Runner(engine, p1, p2).RunMatchAsync(0, config, CancellationToken.None).Result;

            Assert.IsFalse(first.RuleViolationOccurred, first.ErrorMessage);
            Assert.IsNull(first.ErrorMessage, "a clean game reports no error");
            Assert.IsFalse(first.HitTurnLimit, "two heuristics finish well inside the turn limit");
            Assert.AreNotEqual(PieceOwner.None, first.Winner, "the game has to produce a winner");
            Assert.AreNotEqual(WinReason.None, first.WinReason);
            Assert.IsTrue(first.FinalState.gameOver);
            Assert.Greater(first.TotalHalfMoves, 0);
            Assert.AreEqual(first.TotalHalfMoves, first.TotalDiceRolls, "one throw per half-move");
            Assert.IsEmpty(engine.ValidateState(first.FinalState), "the final board has to be sound");

            // The whole point of a seeded harness: the same seed replays the same game.
            Assert.AreEqual(first.Winner, again.Winner);
            Assert.AreEqual(first.WinReason, again.WinReason);
            Assert.AreEqual(first.TotalHalfMoves, again.TotalHalfMoves);
            Assert.AreEqual(first.TotalCaptures, again.TotalCaptures);
        }

        [Test]
        public void MatchRunner_PlaysRandomBotMatchWithoutRuleViolations()
        {
            RulesEngine engine = Engine();
            var p1 = new RandomPlayerAgent(PieceOwner.P1, "Random", new BenchRandomSource(1));
            var p2 = new RandomPlayerAgent(PieceOwner.P2, "Random", new BenchRandomSource(2));

            MatchResult result = Runner(engine, p1, p2)
                .RunMatchAsync(0, Config(20260919, 1000), CancellationToken.None)
                .Result;

            Assert.IsFalse(result.RuleViolationOccurred, result.ErrorMessage);
            Assert.IsNull(result.ErrorMessage);
            Assert.Greater(result.TotalHalfMoves, 0);
            Assert.IsNotNull(result.FinalState);
            Assert.IsEmpty(engine.ValidateState(result.FinalState));
        }

        [Test]
        public void MatchRunner_EnforcesTurnLimit()
        {
            RulesEngine engine = Engine();
            // Two random bots: neither can reach a queen in twenty half-moves, so the cap is what ends this.
            var p1 = new RandomPlayerAgent(PieceOwner.P1, "Random", new BenchRandomSource(11));
            var p2 = new RandomPlayerAgent(PieceOwner.P2, "Random", new BenchRandomSource(12));

            MatchResult result = Runner(engine, p1, p2)
                .RunMatchAsync(0, Config(424242, 20), CancellationToken.None)
                .Result;

            Assert.IsTrue(result.HitTurnLimit, "the game has to stop at the cap");
            Assert.AreEqual(20, result.TotalHalfMoves, "the loop stops exactly at the cap");
            Assert.AreEqual(PieceOwner.None, result.Winner);
            Assert.AreEqual(WinReason.None, result.WinReason);
            Assert.IsFalse(result.FinalState.gameOver, "a game that ran out of turns is not a finished game");
            Assert.IsFalse(result.RuleViolationOccurred, result.ErrorMessage);
            Assert.IsNull(result.ErrorMessage, "a turn-limit draw is a result, not a failure");
            Assert.IsEmpty(engine.ValidateState(result.FinalState));
        }

        [Test]
        public void MatchRunner_HeuristicOutperformsARandomOpponent()
        {
            const int Games = 40;
            const int BaseSeed = 1000;

            // 22 of 40 is a floor chosen a comfortable distance under the measured rate (~70%: 112 of 160
            // games over four seed bases), not a number fitted to one sample.
            const int MinimumHeuristicWins = 22;

            RulesEngine engine = Engine();
            int heuristicWins = 0;

            for (int game = 0; game < Games; ++game)
            {
                // Both seats are played, so a seat's first-move advantage cannot decide the comparison.
                bool heuristicIsP1 = game % 2 == 0;
                int opponentSeed = BaseSeed * 31 + game;

                var heuristic = new HeuristicPlayerAgent(heuristicIsP1 ? PieceOwner.P1 : PieceOwner.P2, "Heuristic", engine);
                var opponent = new KeepTheDiceRandomAgent(heuristicIsP1 ? PieceOwner.P2 : PieceOwner.P1, opponentSeed);
                MatchRunner runner = Runner(
                    engine,
                    heuristicIsP1 ? heuristic : opponent,
                    heuristicIsP1 ? opponent : heuristic);

                MatchResult result = runner
                    .RunMatchAsync(game, Config(BaseSeed + game, 1000), CancellationToken.None)
                    .Result;

                Assert.IsFalse(result.RuleViolationOccurred, "game " + game + ": " + result.ErrorMessage);
                Assert.IsNull(result.ErrorMessage, "game " + game + ": " + result.ErrorMessage);
                Assert.IsEmpty(engine.ValidateState(result.FinalState), "game " + game + " ended on a sound board");

                if (result.Winner != PieceOwner.None && (result.Winner == PieceOwner.P1) == heuristicIsP1) heuristicWins++;
            }

            Assert.GreaterOrEqual(heuristicWins, MinimumHeuristicWins,
                "the heuristic has to win the clear majority of the matchups, but won only " + heuristicWins + " of " + Games);
        }

        // ------------------------------------------------------------------ the failure rules

        [Test]
        public void MatchRunner_RecordsAnIllegalProposalAsARuleViolationAndAborts()
        {
            RulesEngine engine = Engine();
            var runner = Runner(
                engine,
                new IllegalMoveAgent(PieceOwner.P1),
                new HeuristicPlayerAgent(PieceOwner.P2, "Heuristic", engine));

            MatchResult result = runner.RunMatchAsync(0, Config(4242, 500), CancellationToken.None).Result;

            Assert.IsTrue(result.RuleViolationOccurred, "an illegal proposal is a rule violation");
            Assert.IsNotNull(result.ErrorMessage);
            StringAssert.Contains("not legal", result.ErrorMessage);
            Assert.AreEqual(PieceOwner.None, result.Winner);
            Assert.AreEqual(WinReason.None, result.WinReason);
            Assert.IsFalse(result.HitTurnLimit, "the game was aborted, not drawn");
            Assert.Less(result.TotalHalfMoves, 500, "the game stops as soon as the violation happens");
            Assert.IsEmpty(engine.ValidateState(result.FinalState), "the refused move never touched the board");

            // A violation is what makes the run exit non-zero, so the summary has to carry it.
            var summary = new BenchmarkSummary();
            summary.Add(result);
            Assert.AreEqual(1, summary.RuleViolations);
            Assert.AreEqual(1, summary.Failures);
        }

        [Test]
        public void MatchRunner_RecordsAnAgentTimeoutAsAFailureWithoutAViolation()
        {
            RulesEngine engine = Engine();

            MatchConfig config = Config(99, 200);
            config.TurnTimeout = TimeSpan.FromMilliseconds(250);

            var runner = Runner(
                engine,
                new SlowAgent(PieceOwner.P1),
                new HeuristicPlayerAgent(PieceOwner.P2, "Heuristic", engine));

            MatchResult result = runner.RunMatchAsync(0, config, CancellationToken.None).Result;

            Assert.IsFalse(result.RuleViolationOccurred, "a slow agent breaks the deadline, not the rules");
            Assert.IsNotNull(result.ErrorMessage);
            StringAssert.Contains("TimeoutException", result.ErrorMessage);
            Assert.AreEqual(PieceOwner.None, result.Winner);

            var summary = new BenchmarkSummary();
            summary.Add(result);
            Assert.AreEqual(0, summary.RuleViolations);
            Assert.AreEqual(1, summary.Failures, "a failed game has to make the run exit non-zero");
        }

        [Test]
        public void MatchRunner_RejectsAgentsSeatedOnTheWrongSide()
        {
            RulesEngine engine = Engine();
            var first = new HeuristicPlayerAgent(PieceOwner.P1, "Heuristic", engine);
            var second = new HeuristicPlayerAgent(PieceOwner.P2, "Heuristic", engine);

            Assert.Throws<ArgumentException>(delegate { Runner(engine, second, first); });
            Assert.Throws<ArgumentException>(delegate { Runner(engine, first, first); });
            Assert.Throws<ArgumentNullException>(delegate { Runner(engine, first, null); });
        }

        // ------------------------------------------------------------------ the LLM fallback count

        /// <summary>
        /// The counter behind the summary's "LLM fallbacks" line reads the agent's log, so this drives the
        /// real <see cref="LlmPlayerAgent"/> through every place it logs a fallback — two per kind of
        /// decision (no endpoint, an unusable answer, a failed request) — and checks that each one is counted
        /// once and that nothing else is.
        /// </summary>
        [Test]
        public void LlmFallbackCounter_CountsEveryFallbackTheAgentLogs()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);
            Assert.Greater(legalMoves.Count, 1, "the fixture has to offer a move choice");
            Assert.IsTrue(engine.CanReroll(board.State), "the fixture has to offer a re-roll");

            var counter = new LlmFallbackCounter();
            Action<string> log = delegate(string line) { counter.Observe(line); };

            // No endpoint at all.
            LlmAgent(engine, null, log).DecideRerollAsync(board.State, CancellationToken.None).Wait();
            LlmAgent(engine, null, log).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Wait();
            Assert.AreEqual(2, counter.Count, "both 'no endpoint is configured' fallbacks have to be counted");

            // An endpoint that answers, but not with a decision the agent can use.
            var unusable = new ScriptedLlmTransport(Chat("I am not sure what to play."));
            LlmAgent(engine, unusable, log).DecideRerollAsync(board.State, CancellationToken.None).Wait();
            LlmAgent(engine, unusable, log).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Wait();
            Assert.AreEqual(4, counter.Count, "both 'did not answer with a usable ...' fallbacks have to be counted");

            // An endpoint that fails outright, which names the failure it absorbed.
            var failing = new ScriptedLlmTransport(Chat("{}")) { Failure = new HttpRequestException("connection refused") };
            LlmAgent(engine, failing, log).DecideRerollAsync(board.State, CancellationToken.None).Wait();
            LlmAgent(engine, failing, log).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Wait();
            Assert.AreEqual(6, counter.Count, "both 'request failed (...)' fallbacks have to be counted");

            counter.Reset();
            Assert.AreEqual(0, counter.Count, "a run reports each game on its own");
        }

        /// <summary>
        /// The lines the counter reads are not all fallbacks: a decision the model made carries the model's
        /// own reasoning, and a model that echoes the agent's fallback wording there — deliberately, or by
        /// accident — must not move the number. Counting a phrase anywhere in the line did exactly that; the
        /// line's shape does not, because the reasoning is never where a fallback sentence starts.
        /// </summary>
        [Test]
        public void LlmFallbackCounter_IgnoresAFallbackSentenceQuotedInTheModelsReasoning()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);

            var counter = new LlmFallbackCounter();
            Action<string> log = delegate(string line) { counter.Observe(line); };

            var moveEcho = new ScriptedLlmTransport(Chat(
                "{\"move_index\": 1, \"reasoning\": \"I was deciding heuristically, then the endpoint did not answer " +
                "with a usable move index; playing the heuristic choice.\"}"));
            Move chosen = LlmAgent(engine, moveEcho, log)
                .DecideMoveAsync(board.State, legalMoves, CancellationToken.None)
                .Result;

            Assert.AreEqual(legalMoves[1].pieceId, chosen.pieceId, "the model's own answer still decides the move");
            Assert.AreEqual(0, counter.Count, "the model's reasoning is not the agent's fallback");

            var rerollEcho = new ScriptedLlmTransport(Chat(
                "{\"reroll\": false, \"reasoning\": \"no endpoint is configured; deciding heuristically.\"}"));
            Assert.AreEqual(
                RerollDecision.KeepDiceAndProceed,
                LlmAgent(engine, rerollEcho, log).DecideRerollAsync(board.State, CancellationToken.None).Result);
            Assert.AreEqual(0, counter.Count, "a decision line that quotes a fallback is still a decision");
        }

        /// <summary>
        /// The counter counts the agent's own log lines: it needs the <c>&lt;name&gt;: </c> prefix the agent
        /// writes, and it needs the sentence to be where that agent puts it, so a look-alike — a fallback
        /// phrase buried in the middle of a line — is not a fallback.
        /// </summary>
        [Test]
        public void LlmFallbackCounter_NeedsTheAgentsOwnLogLine()
        {
            var counter = new LlmFallbackCounter();

            Assert.IsFalse(counter.Observe(null));
            Assert.IsFalse(counter.Observe(string.Empty));
            Assert.IsFalse(counter.Observe("no endpoint is configured; deciding heuristically."),
                "a line without the agent's name prefix is not the agent's log");
            Assert.IsFalse(counter.Observe("NPC: the endpoint did not answer with a usable move index; playing the heuristic choice"),
                "the sentence has to be the one the agent logs, punctuation and all");
            Assert.IsFalse(counter.Observe("NPC: chose move 0 of 4 (piece 1000 -> place 3): the move request failed (500); playing the heuristic choice."),
                "an echoed sentence sits in the middle of a decision line, where no fallback starts");
            Assert.AreEqual(0, counter.Count);

            Assert.IsTrue(counter.Observe("NPC: no endpoint is configured; deciding heuristically."));
            Assert.IsTrue(counter.Observe("Sáhkku player: the move request failed (connection refused); playing the heuristic choice."),
                "a multi-word name still splits at the first separator");
            Assert.AreEqual(2, counter.Count);
        }

        // ------------------------------------------------------------------ the statistics

        [Test]
        public void BenchmarkStats_AggregatesResultsCorrectly()
        {
            var summary = new BenchmarkSummary();
            summary.Add(Result(0, PieceOwner.P1, WinReason.QueenCaptured, 10, 2, 1));
            summary.Add(Result(1, PieceOwner.P1, WinReason.OpponentSoldiersExhausted, 20, 4, 2));
            summary.Add(Result(2, PieceOwner.P2, WinReason.QueenCaptured, 30, 6, 3));
            summary.Add(Result(3, PieceOwner.None, WinReason.None, 40, 8, 0));

            Assert.AreEqual(4, summary.TotalGames);
            Assert.AreEqual(2, summary.P1Wins);
            Assert.AreEqual(1, summary.P2Wins);
            Assert.AreEqual(1, summary.Draws);
            Assert.AreEqual(0.5, summary.P1WinRate, 1e-9);
            Assert.AreEqual(0.25, summary.P2WinRate, 1e-9);
            Assert.AreEqual(25.0, summary.AverageHalfMoves, 1e-9);
            Assert.AreEqual(10, summary.MinHalfMoves);
            Assert.AreEqual(40, summary.MaxHalfMoves);
            Assert.AreEqual(100, summary.TotalHalfMoves);
            Assert.AreEqual(20, summary.TotalCaptures);
            Assert.AreEqual(6, summary.TotalLlmFallbacks);
            Assert.AreEqual(0, summary.RuleViolations);
            Assert.AreEqual(0, summary.Failures);
            Assert.IsEmpty(summary.Errors);
        }

        [Test]
        public void BenchmarkStats_ReportsFailuresAndFormatsBothForms()
        {
            var summary = new BenchmarkSummary();
            summary.Add(Result(0, PieceOwner.P1, WinReason.QueenCaptured, 10, 2, 0));
            summary.Add(Result(1, PieceOwner.P2, WinReason.QueenCaptured, 20, 4, 0));
            summary.Add(Result(2, PieceOwner.None, WinReason.None, 30, 6, 0));

            var failed = Result(3, PieceOwner.None, WinReason.None, 4, 0, 0);
            failed.RuleViolationOccurred = true;
            failed.ErrorMessage = "rule violation: piece -1 -> place -1 is not legal.";
            summary.Add(failed);

            summary.Add(new MatchResult
            {
                MatchIndex = 4,
                Winner = PieceOwner.None,
                TotalHalfMoves = 7,
                ErrorMessage = "TimeoutException: too slow"
            });

            Assert.AreEqual(5, summary.TotalGames);
            Assert.AreEqual(3, summary.Draws);
            Assert.AreEqual(1, summary.RuleViolations);
            Assert.AreEqual(2, summary.Failures);
            Assert.AreEqual(2, summary.Errors.Count);
            Assert.AreEqual(4, summary.MinHalfMoves, "a failed game still counts towards the range");
            Assert.AreEqual(30, summary.MaxHalfMoves);

            string pretty = summary.ToPrettyString("Heuristic", "Random");
            StringAssert.Contains("Heuristic (P1) vs Random (P2)", pretty);
            StringAssert.Contains("P1 wins (Heuristic)", pretty);
            StringAssert.Contains("rule violations", pretty);
            StringAssert.Contains("! game 3: rule violation: piece -1 -> place -1 is not legal.", pretty);
            StringAssert.Contains("! game 4: TimeoutException: too slow", pretty);

            string json = summary.ToJsonString("Heuristic", "Random");
            StringAssert.Contains("\"p1\":\"Heuristic\"", json);
            StringAssert.Contains("\"p2\":\"Random\"", json);
            StringAssert.Contains("\"totalGames\":5", json);
            StringAssert.Contains("\"p1Wins\":1", json);
            StringAssert.Contains("\"p2Wins\":1", json);
            StringAssert.Contains("\"draws\":3", json);
            StringAssert.Contains("\"p1WinRate\":0.2", json);
            StringAssert.Contains("\"averageHalfMoves\":14.2", json);
            StringAssert.Contains("\"minHalfMoves\":4", json);
            StringAssert.Contains("\"ruleViolations\":1", json);
            StringAssert.Contains("\"failures\":2", json);
            StringAssert.Contains("\"errors\":[\"game 3: rule violation", json);

            // The empty-run summary must still be printable, and must not divide by zero.
            var empty = new BenchmarkSummary();
            Assert.AreEqual(0, empty.P1WinRate, 1e-9);
            Assert.IsNotEmpty(empty.ToPrettyString("Heuristic", "Random"));
            StringAssert.Contains("\"errors\":[]", empty.ToJsonString("Heuristic", "Random"));
        }

        static MatchResult Result(int index, PieceOwner winner, WinReason reason, int halfMoves, int captures, int llmFallbacks)
        {
            return new MatchResult
            {
                MatchIndex = index,
                Winner = winner,
                WinReason = reason,
                TotalHalfMoves = halfMoves,
                TotalCaptures = captures,
                LlmFallbacks = llmFallbacks
            };
        }

        // ------------------------------------------------------------------ the scripted endpoint

        /// <summary>
        /// Stands in for the HTTP transport under the real <see cref="LlmPlayerAgent"/>, so the fallback
        /// count can be pinned without a model or a socket: it answers with a scripted body, or fails with a
        /// scripted exception.
        /// </summary>
        sealed class ScriptedLlmTransport : ILlmTransport
        {
            readonly string responseBody;

            /// <summary>Thrown instead of answering, e.g. an <see cref="HttpRequestException"/> for HTTP 500.</summary>
            public Exception Failure;

            public ScriptedLlmTransport(string responseBody) { this.responseBody = responseBody; }

            public Task<string> PostChatCompletionAsync(string endpointUrl, string requestJson, CancellationToken cancellationToken)
            {
                if (Failure != null) return Task.FromException<string>(Failure);
                return Task.FromResult(responseBody);
            }
        }

        /// <summary>An OpenAI-compatible chat-completion body whose single message is <paramref name="content"/>.</summary>
        static string Chat(string content)
        {
            return "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"choices\":[{\"index\":0,\"message\":" +
                   "{\"role\":\"assistant\",\"content\":" + LlmChatRequest.Quote(content) + "},\"finish_reason\":\"stop\"}]}";
        }

        /// <summary>A board that offers the player a choice of moves and the option to re-throw the sáhkku die.</summary>
        static Board BoardWithChoices(RulesEngine engine)
        {
            var board = new Board(engine);
            board.AddOnArc(10, PieceType.Soldier, PieceOwner.P1, active: true);
            board.AddOnArc(40, PieceType.Soldier, PieceOwner.P1, active: true);
            board.SetDice(DieFace.Sahhku, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1move);
            return board;
        }

        /// <summary>An LLM agent logging into <paramref name="log"/>, which is where the counter reads from.</summary>
        static LlmPlayerAgent LlmAgent(RulesEngine engine, ILlmTransport transport, Action<string> log)
        {
            return new LlmPlayerAgent(PieceOwner.P1, "NPC", transport, new LlmConfig(), engine, log);
        }

        // ------------------------------------------------------------------ the test agents

        /// <summary>
        /// The strength baseline: uniform random legal moves, keeping whatever it rolled. Deterministic
        /// from its seed, and weak by construction — unlike <see cref="RandomPlayerAgent"/>, which is
        /// strong in this ruleset because it re-throws every sáhkku (see the fixture summary).
        /// </summary>
        sealed class KeepTheDiceRandomAgent : IPlayerAgent
        {
            readonly BenchRandomSource random;

            public KeepTheDiceRandomAgent(PieceOwner owner, int seed)
            {
                Owner = owner;
                Name = "Random (keeps the dice)";
                random = new BenchRandomSource(seed);
            }

            public PieceOwner Owner { get; private set; }
            public string Name { get; private set; }

            public Task<RerollDecision> DecideRerollAsync(GameState state, CancellationToken cancellationToken)
            {
                return Task.FromResult(RerollDecision.KeepDiceAndProceed);
            }

            public Task<Move> DecideMoveAsync(GameState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken)
            {
                if (legalMoves == null || legalMoves.Count == 0) return Task.FromResult(default(Move));
                return Task.FromResult(legalMoves[random.NextInt(0, legalMoves.Count)]);
            }
        }

        /// <summary>Proposes a move that cannot be legal, to pin the violation path end to end.</summary>
        sealed class IllegalMoveAgent : IPlayerAgent
        {
            public IllegalMoveAgent(PieceOwner owner) { Owner = owner; }

            public PieceOwner Owner { get; private set; }
            public string Name { get { return "Illegal"; } }

            public Task<RerollDecision> DecideRerollAsync(GameState state, CancellationToken cancellationToken)
            {
                return Task.FromResult(RerollDecision.KeepDiceAndProceed);
            }

            public Task<Move> DecideMoveAsync(GameState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken)
            {
                // Neither -1 is a piece or a place the engine knows, whatever the legal list holds.
                return Task.FromResult(new Move(-1, -1));
            }
        }

        /// <summary>Never answers inside the deadline, to pin the per-turn timeout rule.</summary>
        sealed class SlowAgent : IPlayerAgent
        {
            public SlowAgent(PieceOwner owner) { Owner = owner; }

            public PieceOwner Owner { get; private set; }
            public string Name { get { return "Slow"; } }

            public async Task<RerollDecision> DecideRerollAsync(GameState state, CancellationToken cancellationToken)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                return RerollDecision.KeepDiceAndProceed;
            }

            public async Task<Move> DecideMoveAsync(GameState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                return legalMoves == null || legalMoves.Count == 0 ? default(Move) : legalMoves[0];
            }
        }
    }
}

#endif
