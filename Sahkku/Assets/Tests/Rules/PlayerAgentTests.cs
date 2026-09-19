using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Sahkku.Rules;
using Sahkku.Rules.Bridge;

namespace Sahkku.Rules.Tests
{
    /// <summary>
    /// The player agents behind the match controller. They are Unity-free, which is why the same tests
    /// run in the EditMode runner and headlessly through <c>dotnet test Tools/RulesTests</c>.
    /// </summary>
    [TestFixture]
    public class PlayerAgentTests
    {
        static RulesEngine Engine() { return new RulesEngine(TestRuleset.Load()); }

        /// <summary>Records the ranges it was asked for, so the legacy draw can be pinned.</summary>
        sealed class ScriptedBotRandom : IBotRandomSource
        {
            readonly int[] values;
            int index;

            public ScriptedBotRandom(params int[] values) { this.values = values; }

            public readonly List<int[]> Requests = new List<int[]>();

            public int NextInt(int minimumInclusive, int maximumExclusive)
            {
                Requests.Add(new[] { minimumInclusive, maximumExclusive });
                return values[index++ % values.Length];
            }
        }

        sealed class ScriptedHuman : IHumanInteraction
        {
            public Move MoveToReturn;
            public RerollDecision RerollToReturn = RerollDecision.KeepDiceAndProceed;
            public int MoveRequests;
            public int RerollRequests;

            public Task<RerollDecision> RequestRerollAsync(GameState state, CancellationToken cancellationToken)
            {
                RerollRequests++;
                return Task.FromResult(RerollToReturn);
            }

            public Task<Move> RequestMoveAsync(GameState state, IReadOnlyList<Move> legalMoves, CancellationToken cancellationToken)
            {
                MoveRequests++;
                return Task.FromResult(MoveToReturn);
            }
        }

        // ------------------------------------------------------------------ the heuristic bot

        [Test]
        public void Heuristic_KeepsTheSahhkuWhenItCanActivateAPiece()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(0, PieceType.Soldier, PieceOwner.P1, active: false, activatable: true);
            board.SetDice(DieFace.Sahhku, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1move);

            Assert.IsTrue(engine.CanReroll(board.State), "a sáhkku in the move phase offers a re-roll");
            Assert.AreEqual(RerollDecision.KeepDiceAndProceed, HeuristicPlayerAgent.ChooseReroll(engine, board.State));
        }

        [Test]
        public void Heuristic_ThrowsTheSahhkuAgainWhenNothingIsWaiting()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.SetDice(DieFace.Sahhku, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1move);

            Assert.IsTrue(engine.CanReroll(board.State));
            Assert.AreEqual(RerollDecision.RerollActiveDie, HeuristicPlayerAgent.ChooseReroll(engine, board.State));
        }

        [Test]
        public void Heuristic_TakesTheEnemyQueenOverAnythingElse()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            int queenArc = 20;
            int queenPlace = engine.PlaceOfArc(PieceOwner.P1, queenArc);

            board.AddOnArc(19, PieceType.Soldier, PieceOwner.P1, active: true);
            board.AddOnArc(40, PieceType.Soldier, PieceOwner.P1, active: true); // could simply advance
            board.Add(queenPlace, PieceType.Queen, PieceOwner.P2, active: true);
            board.SetDice(DieFace.Sahhku, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1move);

            List<Move> legalMoves = engine.LegalMoves(board.State);
            Assert.Greater(legalMoves.Count, 1, "the fixture has to offer a choice");

            Move chosen = HeuristicPlayerAgent.ChooseMove(engine, board.State, legalMoves);
            Assert.AreEqual(queenPlace, chosen.targetPlaceIndex);
            Assert.IsTrue(engine.IsLegalMove(board.State, chosen));
        }

        [Test]
        public void Heuristic_CapturesASoldierRatherThanAdvancing()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            int victimPlace = engine.PlaceOfArc(PieceOwner.P1, 21);

            board.AddOnArc(20, PieceType.Soldier, PieceOwner.P1, active: true);
            board.AddOnArc(40, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Add(victimPlace, PieceType.Soldier, PieceOwner.P2, active: true);
            board.SetDice(DieFace.Sahhku, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1move);

            Move chosen = HeuristicPlayerAgent.ChooseMove(engine, board.State, engine.LegalMoves(board.State));
            Assert.AreEqual(victimPlace, chosen.targetPlaceIndex);
        }

        [Test]
        public void Heuristic_PrefersActivatingAPieceOverASimpleAdvance()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);

            board.Add(0, PieceType.Soldier, PieceOwner.P1, active: false, activatable: true);
            board.AddOnArc(40, PieceType.Soldier, PieceOwner.P1, active: true);
            board.SetDice(DieFace.Sahhku, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1move);

            Move chosen = HeuristicPlayerAgent.ChooseMove(engine, board.State, engine.LegalMoves(board.State));
            Piece moved = engine.FindPiece(board.State, chosen.pieceId);
            Assert.AreEqual(0, moved.arc, "the waiting soldier is the one that should be activated");
        }

        [Test]
        public void Heuristic_PlaysAWholeMatchWithoutBreakingTheRules()
        {
            RulesEngine engine = Engine();
            var random = new SeededRandomSource(20260919);
            GameState state = engine.InitGame(new EngineOptions(PieceOwner.P1, true), random);

            for (int halfMove = 0; halfMove < 20000 && !state.gameOver; ++halfMove)
            {
                if (state.IsRollPhase) engine.RollDice(state, random);

                for (int attempt = 0; attempt < 32 && !state.gameOver && engine.CanReroll(state); ++attempt)
                {
                    RerollDecision decision = HeuristicPlayerAgent.ChooseReroll(engine, state);
                    engine.ApplyRerollDecision(
                        state,
                        decision == RerollDecision.RerollActiveDie ? (IRandomSource)random : null,
                        decision);
                }
                if (state.gameOver) break;

                while (!state.gameOver && !state.IsRollPhase)
                {
                    List<Move> legalMoves = engine.LegalMoves(state);
                    if (legalMoves.Count == 0)
                    {
                        engine.NextPlayerTurn(state);
                        break;
                    }

                    Move move = HeuristicPlayerAgent.ChooseMove(engine, state, legalMoves);
                    Assert.IsTrue(engine.IsLegalMove(state, move), "the heuristic only proposes legal moves");
                    engine.ApplyMove(state, move);
                    CollectionAssert.IsEmpty(engine.ValidateState(state), "the state stays sound after every move");
                }
            }

            Assert.IsTrue(state.gameOver, "the heuristic bots have to be able to finish a game");
        }

        // ------------------------------------------------------------------ the other agents

        [Test]
        public void Random_KeepsTheLegacyDrawAndStaysLegal()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.AddOnArc(10, PieceType.Soldier, PieceOwner.P1, active: true);
            board.AddOnArc(40, PieceType.Soldier, PieceOwner.P1, active: true);
            board.SetDice(DieFace.Sahhku, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1move);

            List<Move> legalMoves = engine.LegalMoves(board.State);
            var random = new ScriptedBotRandom(1, 3);
            var agent = new RandomPlayerAgent(PieceOwner.P1, "Random", random);

            Move chosen = agent.DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Result;

            Assert.IsTrue(engine.IsLegalMove(board.State, chosen));
            Assert.AreEqual(2, random.Requests.Count, "one draw for the piece, one for the move index");
            Assert.AreEqual(0, random.Requests[0][0]);
            Assert.AreEqual(4, random.Requests[1][1], "the move index is still drawn from [0, 4) and clamped");
        }

        [Test]
        public void Random_AlwaysThrowsTheSahhkuAgain()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.SetDice(DieFace.Sahhku, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1move);

            var agent = new RandomPlayerAgent(PieceOwner.P1, "Random", new ScriptedBotRandom(0));
            Assert.AreEqual(RerollDecision.RerollActiveDie, agent.DecideRerollAsync(board.State, CancellationToken.None).Result);
        }

        [Test]
        public void Human_DelegatesBothDecisionsToTheInteraction()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.AddOnArc(10, PieceType.Soldier, PieceOwner.P1, active: true);
            board.SetDice(DieFace.Sahhku, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1move);

            List<Move> legalMoves = engine.LegalMoves(board.State);
            var interaction = new ScriptedHuman
            {
                MoveToReturn = legalMoves[0],
                RerollToReturn = RerollDecision.RerollActiveDie
            };
            var agent = new HumanPlayerAgent(PieceOwner.P1, "Player 1", interaction);

            Assert.AreEqual(PieceOwner.P1, agent.Owner);
            Assert.AreEqual("Player 1", agent.Name);
            Assert.AreEqual(RerollDecision.RerollActiveDie, agent.DecideRerollAsync(board.State, CancellationToken.None).Result);
            Assert.AreEqual(legalMoves[0].targetPlaceIndex, agent.DecideMoveAsync(board.State, legalMoves, CancellationToken.None).Result.targetPlaceIndex);
            Assert.AreEqual(1, interaction.RerollRequests);
            Assert.AreEqual(1, interaction.MoveRequests);
        }

        [Test]
        public void Agents_DoNotDecideWhenThereIsNothingToDecide()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Phase(TurnPhase.P1move);

            var random = new RandomPlayerAgent(PieceOwner.P1, "Random", new ScriptedBotRandom(0));
            var heuristic = new HeuristicPlayerAgent(PieceOwner.P1, "Heuristic", engine);

            Assert.AreEqual(default(Move), random.DecideMoveAsync(board.State, new List<Move>(), CancellationToken.None).Result);
            Assert.AreEqual(default(Move), heuristic.DecideMoveAsync(board.State, new List<Move>(), CancellationToken.None).Result);
        }
    }
}
