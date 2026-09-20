using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sahkku.Rules;
using Sahkku.Rules.Bridge;

namespace Sahkku.Rules.Tests
{
    /// <summary>
    /// The LLM NPC agent. Everything here runs offline: the endpoint is a scripted
    /// <see cref="ILlmTransport"/>, so the fallback matrix (HTTP error, timeout, malformed answer,
    /// out-of-range index, cancellation) is pinned without a model or a server. The agent lives in
    /// <c>RulesBridge</c> and only depends on <c>System.*</c>, which is why the same tests also run in the
    /// Unity EditMode runner.
    /// </summary>
    [TestFixture]
    public class LlmAgentTests
    {
        static RulesEngine Engine() { return new RulesEngine(TestRuleset.Load()); }

        /// <summary>
        /// Stands in for the HTTP transport: records every request and answers with a scripted body, or
        /// fails with a scripted exception. Nothing here touches a socket.
        /// </summary>
        sealed class MockLlmTransport : ILlmTransport
        {
            readonly Func<string, string> respond;

            public readonly List<string> Endpoints = new List<string>();
            public readonly List<string> Requests = new List<string>();

            /// <summary>Thrown instead of answering, e.g. an <see cref="HttpRequestException"/> for HTTP 500.</summary>
            public Exception Failure;

            public MockLlmTransport(string responseBody)
                : this(delegate { return responseBody; }) { }

            public MockLlmTransport(Func<string, string> respond)
            {
                this.respond = respond;
            }

            public int Calls { get { return Requests.Count; } }

            public Task<string> PostChatCompletionAsync(string endpointUrl, string requestJson, CancellationToken cancellationToken)
            {
                Endpoints.Add(endpointUrl);
                Requests.Add(requestJson);
                if (Failure != null) return Task.FromException<string>(Failure);
                return Task.FromResult(respond(requestJson));
            }
        }

        /// <summary>An OpenAI-compatible chat-completion body whose single message is <paramref name="content"/>.</summary>
        static string Chat(string content)
        {
            return "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"choices\":[{\"index\":0,\"message\":" +
                   "{\"role\":\"assistant\",\"content\":" + LlmChatRequest.Quote(content) + "},\"finish_reason\":\"stop\"}]}";
        }

        static Board BoardWithChoices(RulesEngine engine)
        {
            var board = new Board(engine);
            board.AddOnArc(10, PieceType.Soldier, PieceOwner.P1, active: true);
            board.AddOnArc(40, PieceType.Soldier, PieceOwner.P1, active: true);
            board.SetDice(DieFace.Sahhku, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1move);
            return board;
        }

        /// <summary>A board whose throw offers no re-roll at all (no sáhkku is up).</summary>
        static Board BoardWithoutReroll(RulesEngine engine)
        {
            var board = new Board(engine);
            board.AddOnArc(10, PieceType.Soldier, PieceOwner.P1, active: true);
            board.SetDice(DieFace.Zero, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1move);
            return board;
        }

        static LlmPlayerAgent Agent(RulesEngine engine, ILlmTransport transport, Action<string> log = null)
        {
            return new LlmPlayerAgent(PieceOwner.P1, "NPC", transport, new LlmConfig(), engine, log);
        }

        static void AssertSameMove(Move expected, Move actual)
        {
            Assert.AreEqual(expected.pieceId, actual.pieceId);
            Assert.AreEqual(expected.targetPlaceIndex, actual.targetPlaceIndex);
        }

        // ------------------------------------------------------------------ reading the model's answer

        [Test]
        public void DecideMove_UsesTheIndexTheEndpointReturned()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);
            Assert.Greater(legalMoves.Count, 1, "the fixture has to offer a choice");

            var transport = new MockLlmTransport(Chat("{\"move_index\": 1, \"reasoning\": \"capturing\"}"));
            Move chosen = Agent(engine, transport).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Result;

            AssertSameMove(legalMoves[1], chosen);
            Assert.IsTrue(engine.IsLegalMove(board.State, chosen), "an index into the legal list is legal by construction");
            Assert.AreEqual(1, transport.Calls);
        }

        [Test]
        public void DecideMove_SendsTheBoardContextAndTheJSONContract()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);

            var transport = new MockLlmTransport(Chat("{\"move_index\": 0}"));
            Agent(engine, transport).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Wait();

            string request = transport.Requests[0];
            Assert.AreEqual("http://localhost:1234/v1/chat/completions", transport.Endpoints[0]);
            StringAssert.Contains("\"model\":\"pairflow-player\"", request);
            StringAssert.Contains("\"stream\":false", request);
            StringAssert.Contains("## Legal moves", request, "the engine's own move list is what the model ranks");
            StringAssert.Contains("move_index", request);
            StringAssert.Contains("#" + legalMoves[0].pieceId, request, "the numbered list describes the piece the engine found");
        }

        [Test]
        public void DecideMove_ReadsMarkdownFencedJSON()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);

            var transport = new MockLlmTransport(Chat("```json\n{\"move_index\": 1, \"reasoning\": \"attack\"}\n```"));
            Move chosen = Agent(engine, transport).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Result;

            AssertSameMove(legalMoves[1], chosen);
        }

        [Test]
        public void DecideMove_ReadsJSONOutOfConversationalProse()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);

            var transport = new MockLlmTransport(Chat(
                "Sure! Let me think about it.\nThe best option is {\"move_index\": 1, \"reasoning\": \"advance\"} because it attacks."));
            Move chosen = Agent(engine, transport).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Result;

            AssertSameMove(legalMoves[1], chosen);
        }

        [Test]
        public void DecideMove_ReadsAPlainDecisionBodyWithoutTheEnvelope()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);

            var transport = new MockLlmTransport("{\"move_index\": 0, \"reasoning\": \"activate\"}");
            Move chosen = Agent(engine, transport).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Result;

            AssertSameMove(legalMoves[0], chosen);
        }

        [Test]
        public void Parser_AcceptsTheTolerantFormsSmallModelsEmit()
        {
            int index;
            bool reroll;

            Assert.IsTrue(LlmResponseParser.TryParseMoveIndex(Chat("{\"moveIndex\": \"2\"}"), 3, out index));
            Assert.AreEqual(2, index);
            Assert.IsFalse(LlmResponseParser.TryParseMoveIndex(Chat("{\"move_index\": 3}"), 3, out index), "the list has three moves: 0..2");
            Assert.IsFalse(LlmResponseParser.TryParseMoveIndex(Chat("{\"move_index\": -1}"), 3, out index));
            Assert.IsFalse(LlmResponseParser.TryParseMoveIndex(Chat("{\"move_index\": 1.5}"), 3, out index));
            Assert.IsFalse(LlmResponseParser.TryParseMoveIndex(Chat("no JSON at all"), 3, out index));
            Assert.IsFalse(LlmResponseParser.TryParseMoveIndex(null, 3, out index));

            Assert.IsTrue(LlmResponseParser.TryParseReroll(Chat("{\"reroll\": \"true\"}"), out reroll));
            Assert.IsTrue(reroll);
            Assert.IsTrue(LlmResponseParser.TryParseReroll(Chat("{\"reroll\": false}"), out reroll));
            Assert.IsFalse(reroll);
            Assert.IsFalse(LlmResponseParser.TryParseReroll(Chat("{\"reroll\": \"maybe\"}"), out reroll));
            Assert.IsFalse(LlmResponseParser.TryParseReroll(Chat("{\"move_index\": 1}"), out reroll));
        }

        [Test]
        public void Parser_StaysTotalWhenTheAnswerIsNotReadableJSON()
        {
            int index;
            bool reroll;

            // The engine's reader guards malformed JSON with a RuleSetException, but a malformed \u escape
            // (a model writing a Windows path inside its reasoning, say) leaves it as a FormatException. The
            // parser promises "false, never an exception", so both have to mean the same thing: the answer
            // cannot be used. Without this the agent still recovers (it catches everything), but it reports a
            // network-style failure for what is really an unusable answer, and every other caller of the
            // parser - the task-4 harness, for one - gets an exception it was told could not happen.
            Assert.IsFalse(LlmResponseParser.TryParseMoveIndex(
                Chat("{\"move_index\": 1, \"reasoning\": \"C:\\users\\bob\"}"), 3, out index));
            Assert.IsFalse(LlmResponseParser.TryParseMoveIndex("{\"reasoning\": \"\\uZZZZ\"}", 3, out index));
            Assert.IsFalse(LlmResponseParser.TryParseReroll(Chat("{\"reroll\": true, \"reasoning\": \"\\uZZZZ\"}"), out reroll));
            Assert.IsNull(LlmResponseParser.ReadReasoning(Chat("{\"reasoning\": \"\\uZZZZ\"}")));
            // An unreadable body is not an envelope, so it stays its own decision text (as documented) -
            // what matters is that reading it back does not throw either.
            Assert.AreEqual("{\"reasoning\": \"\\uZZZZ\"}", LlmResponseParser.DecisionText("{\"reasoning\": \"\\uZZZZ\"}"));

            // An unreadable candidate is skipped, not fatal: a readable one behind it still answers.
            Assert.IsTrue(LlmResponseParser.TryParseMoveIndex(
                "{\"reasoning\": \"\\uZZZZ\"} and the answer is {\"move_index\": 2}", 3, out index));
            Assert.AreEqual(2, index);
        }

        // ------------------------------------------------------------------ the fallback matrix

        [Test]
        public void DecideMove_FallsBackToTheHeuristicWhenTheRequestFails()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);
            Move heuristic = HeuristicPlayerAgent.ChooseMove(engine, board.State, legalMoves);

            var transport = new MockLlmTransport(Chat("{}"))
            {
                Failure = new HttpRequestException("Response status code does not indicate success: 500 (Internal Server Error).")
            };
            Move chosen = Agent(engine, transport).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Result;

            AssertSameMove(heuristic, chosen);
            Assert.AreEqual(1, transport.Calls);
        }

        [Test]
        public void DecideMove_FallsBackToTheHeuristicWhenTheRequestTimesOut()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);
            Move heuristic = HeuristicPlayerAgent.ChooseMove(engine, board.State, legalMoves);

            // An HttpClient deadline surfaces as a TaskCanceledException whose token is *not* the caller's,
            // which is exactly how the agent tells a slow model from a cancelled match.
            var transport = new MockLlmTransport(Chat("{}")) { Failure = new TaskCanceledException("The request timed out.") };
            Move chosen = Agent(engine, transport).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Result;

            AssertSameMove(heuristic, chosen);
        }

        [Test]
        public void DecideMove_FallsBackToTheHeuristicOnAnUnusableAnswer()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);
            Move heuristic = HeuristicPlayerAgent.ChooseMove(engine, board.State, legalMoves);

            string[] unusable =
            {
                Chat("I am not sure what to play."),                       // no JSON at all
                Chat("{\"move_index\": 99}"),                              // out of range
                Chat("{\"moveindex\": 1}"),                                // misspelled key
                Chat("{\"move_index\": }"),                                // truncated JSON
                Chat("{\"move_index\": 1")                                 // unterminated
            };

            foreach (string body in unusable)
            {
                var transport = new MockLlmTransport(body);
                Move chosen = Agent(engine, transport).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Result;
                AssertSameMove(heuristic, chosen);
            }
        }

        [Test]
        public void DecideMove_PlaysAForcedMoveWithoutAskingTheEndpoint()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            var forced = new List<Move> { new Move(1000, engine.PlaceOfArc(PieceOwner.P1, 11)) };

            var transport = new MockLlmTransport("not even JSON");
            Move chosen = Agent(engine, transport).DecideMoveAsync(board.State, forced, CancellationToken.None).Result;

            AssertSameMove(forced[0], chosen);
            Assert.AreEqual(0, transport.Calls, "a forced move is not a decision");
        }

        [Test]
        public void DecideMove_DoesNotDecideWhenThereIsNothingToDecide()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            var transport = new MockLlmTransport(Chat("{\"move_index\": 0}"));

            Move chosen = Agent(engine, transport).DecideMoveAsync(board.State, new List<Move>(), CancellationToken.None).Result;

            Assert.AreEqual(default(Move), chosen);
            Assert.AreEqual(0, transport.Calls);
        }

        [Test]
        public void DecideMove_RethrowsWhenTheMatchIsCancelled()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);

            var transport = new MockLlmTransport(Chat("{\"move_index\": 0}"));
            var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await Agent(engine, transport).DecideMoveAsync(board.State, legalMoves, cancelled.Token));
            Assert.AreEqual(0, transport.Calls, "a cancelled match must not reach the network");
        }

        [Test]
        public void DecideMove_FallsBackWithoutAnEndpoint()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);

            var agent = new LlmPlayerAgent(PieceOwner.P1, null, null, null, engine);
            Move chosen = agent.DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Result;

            AssertSameMove(HeuristicPlayerAgent.ChooseMove(engine, board.State, legalMoves), chosen);
            Assert.AreEqual("LLM NPC", agent.Name);
            Assert.AreEqual(PieceOwner.P1, agent.Owner);
        }

        // ------------------------------------------------------------------ the re-roll decision

        [Test]
        public void DecideReroll_UsesTheEndpointAnswer()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            Assert.IsTrue(engine.CanReroll(board.State), "the fixture has to offer a re-roll");

            var rerollTransport = new MockLlmTransport(Chat("{\"reroll\": true, \"reasoning\": \"want a better die\"}"));
            Assert.AreEqual(
                RerollDecision.RerollActiveDie,
                Agent(engine, rerollTransport).DecideRerollAsync(board.State, CancellationToken.None).Result);
            StringAssert.Contains("## Dice in spending order", rerollTransport.Requests[0]);

            var keepTransport = new MockLlmTransport(Chat("{\"reroll\": false, \"reasoning\": \"the sáhkku activates a soldier\"}"));
            Assert.AreEqual(
                RerollDecision.KeepDiceAndProceed,
                Agent(engine, keepTransport).DecideRerollAsync(board.State, CancellationToken.None).Result);
        }

        [Test]
        public void DecideReroll_FallsBackToTheHeuristicWhenTheRequestFails()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);

            var transport = new MockLlmTransport(Chat("{}")) { Failure = new HttpRequestException("connection refused") };
            RerollDecision decision = Agent(engine, transport).DecideRerollAsync(board.State, CancellationToken.None).Result;

            Assert.AreEqual(HeuristicPlayerAgent.ChooseReroll(engine, board.State), decision);
        }

        [Test]
        public void DecideReroll_DoesNotAskWhenTheEngineOffersNoReroll()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithoutReroll(engine);
            Assert.IsFalse(engine.CanReroll(board.State));

            var transport = new MockLlmTransport(Chat("{\"reroll\": true}"));
            RerollDecision decision = Agent(engine, transport).DecideRerollAsync(board.State, CancellationToken.None).Result;

            Assert.AreEqual(RerollDecision.KeepDiceAndProceed, decision);
            Assert.AreEqual(0, transport.Calls, "a re-roll the engine would refuse is not worth a round trip");
        }

        // ------------------------------------------------------------------ observability

        [Test]
        public void Agent_LogsItsDecisionAndTheFallback()
        {
            RulesEngine engine = Engine();
            Board board = BoardWithChoices(engine);
            List<Move> legalMoves = engine.LegalMoves(board.State);

            var decisions = new List<string>();
            var transport = new MockLlmTransport(Chat("{\"move_index\": 1, \"reasoning\": \"capturing the queen\"}"));
            Agent(engine, transport, decisions.Add).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Wait();

            Assert.AreEqual(1, decisions.Count);
            StringAssert.Contains("NPC", decisions[0]);
            StringAssert.Contains("capturing the queen", decisions[0]);

            var failures = new List<string>();
            var failing = new MockLlmTransport(Chat("{}")) { Failure = new HttpRequestException("boom") };
            Agent(engine, failing, failures.Add).DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Wait();

            Assert.AreEqual(1, failures.Count, "a fallback is reported exactly once");
            StringAssert.Contains("boom", failures[0]);
            StringAssert.Contains("heuristic", failures[0]);
        }

        // ------------------------------------------------------------------ the HTTP transport

        [Test]
        public void HttpTransport_DoesNotDisposeACallerOwnedClient()
        {
            var client = new HttpClient();
            using (var transport = new HttpClientLlmTransport(client, TimeSpan.FromSeconds(2)))
            {
                Assert.AreEqual(TimeSpan.FromSeconds(2), transport.RequestTimeout);
            }

            // The client outlives the transport, so it must still be usable (no ObjectDisposedException).
            Assert.DoesNotThrow(delegate { client.Timeout = TimeSpan.FromSeconds(30); });
            client.Dispose();
        }

        [Test]
        public void HttpTransport_DisposesTheClientItCreated()
        {
            var transport = new HttpClientLlmTransport();
            transport.Dispose();

            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await transport.PostChatCompletionAsync("http://127.0.0.1:1/v1/chat/completions", "{}", CancellationToken.None));
        }
    }
}
