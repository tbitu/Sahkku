using System.Collections.Generic;
using NUnit.Framework;
using Sahkku.Rules;

namespace Sahkku.Rules.Tests
{
    /// <summary>A bare board with no pieces, for exercising movement/capture rules in isolation.</summary>
    sealed class Board
    {
        public readonly RulesEngine Engine;
        public readonly GameState State;

        int nextId = 1000;

        public Board(RulesEngine engine, PieceOwner startingPlayer = PieceOwner.P1)
        {
            Engine = engine;
            State = engine.InitGame(new EngineOptions(startingPlayer, false));
            foreach (Place place in State.places) place.pieces.Clear();
        }

        public Piece Add(int placeIndex, PieceType type, PieceOwner owner, bool active = false, bool activatable = false)
        {
            var piece = new Piece(nextId++, placeIndex, type, owner, active);
            piece.canBeActivated = activatable;
            State.places[placeIndex].pieces.Add(piece);
            return piece;
        }

        /// <summary>Sets the dice and resets the active die to the first one.</summary>
        public void SetDice(params DieFace[] faces)
        {
            State.dice.Clear();
            State.dice.AddRange(faces);
            State.currentActiveDie = 0;
        }

        public void Phase(TurnPhase phase) { State.turnPhase = phase; }
        public void ActiveDie(int index) { State.currentActiveDie = index; }
    }

    sealed class SequenceRandomSource : IRandomSource
    {
        readonly int[] values;
        int index;

        public SequenceRandomSource(params int[] values) { this.values = values; }

        public int NextDieFaceIndex() { return values[index++ % values.Length]; }
    }

    sealed class SeededRandomSource : IRandomSource
    {
        readonly System.Random random;

        public SeededRandomSource(int seed) { random = new System.Random(seed); }

        public int NextDieFaceIndex() { return random.Next(0, 4); }
        public int Next(int max) { return random.Next(max); }
    }

    static class Events
    {
        public static bool Has(List<RuleEvent> events, RuleEventKind kind)
        {
            foreach (RuleEvent ruleEvent in events)
            {
                if (ruleEvent.kind == kind) return true;
            }
            return false;
        }
    }

    [TestFixture]
    public class SetupTests
    {
        static RulesEngine Engine() { return new RulesEngine(TestRuleset.Load()); }

        [Test]
        public void IndexFromCoordinates_MatchesTheSnakeLayout()
        {
            RulesEngine engine = Engine();
            Assert.AreEqual(0, engine.IndexFromCoordinates(0, 0));
            Assert.AreEqual(14, engine.IndexFromCoordinates(14, 0));
            Assert.AreEqual(15, engine.IndexFromCoordinates(14, 1));
            Assert.AreEqual(29, engine.IndexFromCoordinates(0, 1));
            Assert.AreEqual(30, engine.IndexFromCoordinates(0, 2));
            Assert.AreEqual(44, engine.IndexFromCoordinates(14, 2));

            // Queens (x=11 P1, x=3 P2) and the king (centre) on the middle row.
            Assert.AreEqual(18, engine.IndexFromCoordinates(11, 1));
            Assert.AreEqual(26, engine.IndexFromCoordinates(3, 1));
            Assert.AreEqual(22, engine.IndexFromCoordinates(7, 1));

            Assert.AreEqual(-1, engine.IndexFromCoordinates(-1, 0));
            Assert.AreEqual(-1, engine.IndexFromCoordinates(15, 0));
            Assert.AreEqual(-1, engine.IndexFromCoordinates(0, -1));
            Assert.AreEqual(-1, engine.IndexFromCoordinates(0, 3));
        }

        [Test]
        public void InitGame_Standard_PlacesPiecesAndActivation()
        {
            GameState state = Engine().InitGame(new EngineOptions(PieceOwner.P1, false));

            Assert.AreEqual(45, state.places.Count);
            Assert.AreEqual(3, state.dice.Count);
            foreach (DieFace face in state.dice) Assert.AreEqual(DieFace.Zero, face);
            Assert.AreEqual(TurnPhase.P1roll, state.turnPhase);
            Assert.AreEqual(PieceOwner.P1, state.CurrentPlayer);

            for (int i = 0; i <= 14; ++i)
            {
                Place place = state.places[i];
                Assert.AreEqual(1, place.pieces.Count, "place " + i);
                Assert.AreEqual(PieceType.Soldier, place.pieces[0].type);
                Assert.AreEqual(PieceOwner.P1, place.pieces[0].owner);
                Assert.IsFalse(place.pieces[0].isActive);
                Assert.AreEqual(i == 14, place.pieces[0].canBeActivated, "canBeActivated at " + i);
            }

            for (int i = 30; i <= 44; ++i)
            {
                Place place = state.places[i];
                Assert.AreEqual(1, place.pieces.Count, "place " + i);
                Assert.AreEqual(PieceType.Soldier, place.pieces[0].type);
                Assert.AreEqual(PieceOwner.P2, place.pieces[0].owner);
                Assert.IsFalse(place.pieces[0].isActive);
                Assert.AreEqual(i == 30, place.pieces[0].canBeActivated, "canBeActivated at " + i);
            }

            Assert.AreEqual(PieceType.Queen, state.places[18].pieces[0].type);
            Assert.AreEqual(PieceOwner.P1, state.places[18].pieces[0].owner);
            Assert.IsTrue(state.places[18].pieces[0].canBeActivated);

            Assert.AreEqual(PieceType.Queen, state.places[26].pieces[0].type);
            Assert.AreEqual(PieceOwner.P2, state.places[26].pieces[0].owner);
            Assert.IsTrue(state.places[26].pieces[0].canBeActivated);

            Assert.AreEqual(PieceType.King, state.places[22].pieces[0].type);
            Assert.AreEqual(PieceOwner.None, state.places[22].pieces[0].owner);
            Assert.IsFalse(state.places[22].pieces[0].isActive);
            Assert.IsFalse(state.places[22].pieces[0].canBeActivated);

            // Standard variant: only the two outermost home soldiers may be activated.
            Assert.IsTrue(state.places[14].pieces[0].canBeActivated);
            Assert.IsTrue(state.places[30].pieces[0].canBeActivated);
        }

        [Test]
        public void InitGame_EvenOdds_UsesTheAlternateStartingPositions()
        {
            GameState state = Engine().InitGame(new EngineOptions(PieceOwner.P1, true));

            int[] active = { 14, 30, 13, 31, 12, 32 };
            foreach (int index in active)
            {
                Assert.IsTrue(state.places[index].pieces[0].isActive, "active " + index);
                Assert.IsFalse(state.places[index].pieces[0].canBeActivated, "active " + index);
            }

            Assert.IsTrue(state.places[11].pieces[0].canBeActivated);
            Assert.IsTrue(state.places[33].pieces[0].canBeActivated);

            Assert.IsTrue(state.places[18].pieces[0].canBeActivated);
            Assert.IsTrue(state.places[26].pieces[0].canBeActivated);
        }

        [Test]
        public void InitGame_RespectsStartingPlayer()
        {
            GameState state = Engine().InitGame(new EngineOptions(PieceOwner.P2, false));
            Assert.AreEqual(TurnPhase.P2roll, state.turnPhase);
            Assert.AreEqual(PieceOwner.P2, state.CurrentPlayer);
        }
    }

    [TestFixture]
    public class MovementTests
    {
        static RulesEngine Engine() { return new RulesEngine(TestRuleset.Load()); }

        [Test]
        public void Soldier_MovesForwardByTheDieValue()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);

            Piece piece = board.State.places[5].pieces[0];

            board.SetDice(DieFace.Sahhku);
            CollectionAssert.AreEqual(new[] { 6 }, engine.GetAllowedPlaces(board.State, piece));

            board.SetDice(DieFace.Three);
            CollectionAssert.AreEqual(new[] { 8 }, engine.GetAllowedPlaces(board.State, piece));

            board.SetDice(DieFace.Two);
            CollectionAssert.AreEqual(new[] { 7 }, engine.GetAllowedPlaces(board.State, piece));

            board.SetDice(DieFace.Zero);
            Assert.IsEmpty(engine.GetAllowedPlaces(board.State, piece));
        }

        [Test]
        public void InactiveSoldier_NeedsSahhkuAndActivation()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece piece = board.Add(5, PieceType.Soldier, PieceOwner.P1, active: false, activatable: false);
            board.Phase(TurnPhase.P1move);

            board.SetDice(DieFace.Sahhku);
            Assert.IsEmpty(engine.GetAllowedPlaces(board.State, piece));

            piece.canBeActivated = true;
            CollectionAssert.AreEqual(new[] { 6 }, engine.GetAllowedPlaces(board.State, piece));

            board.SetDice(DieFace.Three);
            Assert.IsEmpty(engine.GetAllowedPlaces(board.State, piece));
        }

        [Test]
        public void PlayerTwo_MovesInTheOppositeDirection()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine, PieceOwner.P2);
            board.Add(44, PieceType.Soldier, PieceOwner.P2, active: true);
            board.Phase(TurnPhase.P2move);
            board.SetDice(DieFace.Sahhku);

            Piece piece = board.State.places[44].pieces[0];
            CollectionAssert.AreEqual(new[] { 43 }, engine.GetAllowedPlaces(board.State, piece));
        }

        [Test]
        public void Soldier_AtTheBoardEdge_HasNoForwardMove()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(44, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            Assert.IsEmpty(engine.GetAllowedPlaces(board.State, board.State.places[44].pieces[0]));
        }

        [Test]
        public void Queen_MovesForwardBackwardAndVertically()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(22, PieceType.Queen, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            // 22 = (7,1): forward 23, backward 21, vertical up 37 = (7,2), vertical down 7 = (7,0).
            CollectionAssert.AreEqual(new[] { 23, 21, 37, 7 }, engine.GetAllowedPlaces(board.State, board.State.places[22].pieces[0]));
        }

        [Test]
        public void Queen_SkipsVerticalMovesThatLeaveTheBoard()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(7, PieceType.Queen, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            // 7 = (7,0): forward 8, backward 6, vertical up 22, vertical down off-board.
            CollectionAssert.AreEqual(new[] { 8, 6, 22 }, engine.GetAllowedPlaces(board.State, board.State.places[7].pieces[0]));
        }

        [Test]
        public void King_MovesLikeAQueenOnceActive()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(22, PieceType.King, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            CollectionAssert.AreEqual(new[] { 23, 21, 37, 7 }, engine.GetAllowedPlaces(board.State, board.State.places[22].pieces[0]));
        }

        [Test]
        public void Soldier_CannotLandOnOwnQueenOrKing()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Add(6, PieceType.Queen, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            Assert.IsEmpty(engine.GetAllowedPlaces(board.State, board.State.places[5].pieces[0]));
        }

        [Test]
        public void Queen_CannotLandOnOwnUnits()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Queen, PieceOwner.P1, active: true);
            board.Add(6, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            List<int> allowed = engine.GetAllowedPlaces(board.State, board.State.places[5].pieces[0]);
            CollectionAssert.DoesNotContain(allowed, 6);
        }

        [Test]
        public void Soldier_MayStackOnOwnActiveSoldier()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Add(6, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            CollectionAssert.AreEqual(new[] { 6 }, engine.GetAllowedPlaces(board.State, board.State.places[5].pieces[0]));
        }

        [Test]
        public void Soldier_CannotLandOnInactiveOpponent()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Add(6, PieceType.Soldier, PieceOwner.P2, active: false);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            Assert.IsEmpty(engine.GetAllowedPlaces(board.State, board.State.places[5].pieces[0]));
        }

        [Test]
        public void GetLegalMoves_FlattensAllowedPlaces()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Add(7, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);
            engine.EvaluateAllowedPlaces(board.State);

            List<Move> moves = engine.GetLegalMoves(board.State);
            Assert.AreEqual(2, moves.Count);
            Assert.IsTrue(moves.Exists(m => m.targetPlaceIndex == 6));
            Assert.IsTrue(moves.Exists(m => m.targetPlaceIndex == 8));
        }
    }

    [TestFixture]
    public class CaptureTests
    {
        static RulesEngine Engine() { return new RulesEngine(TestRuleset.Load()); }

        static Piece PrepareSoldierCapture(RulesEngine engine, Board board, PieceType targetType, PieceOwner targetOwner, bool targetActive)
        {
            Piece attacker = board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Add(6, targetType, targetOwner, active: targetActive);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);
            engine.EvaluateAllowedPlaces(board.State);
            return attacker;
        }

        [Test]
        public void CapturingASoldier_ScoresAndRemovesIt()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece attacker = PrepareSoldierCapture(engine, board, PieceType.Soldier, PieceOwner.P2, true);

            List<RuleEvent> events = engine.ApplyMove(board.State, new Move(attacker.id, 6));

            Assert.IsTrue(Events.Has(events, RuleEventKind.SoldierCaptured));
            Assert.IsFalse(Events.Has(events, RuleEventKind.PieceMoved));
            Assert.AreEqual(1, board.State.p1Captures);
            Assert.AreEqual(0, board.State.p2Captures);
            Assert.AreEqual(6, attacker.placeIndex);
            Assert.AreEqual(1, board.State.places[6].pieces.Count);
        }

        [Test]
        public void FifteenthSoldierCapture_EndsTheGame()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece attacker = PrepareSoldierCapture(engine, board, PieceType.Soldier, PieceOwner.P2, true);
            board.State.p1Captures = 14;

            List<RuleEvent> events = engine.ApplyMove(board.State, new Move(attacker.id, 6));

            Assert.AreEqual(15, board.State.p1Captures);
            Assert.IsTrue(board.State.gameOver);
            Assert.AreEqual(PieceOwner.P1, board.State.winner);
            Assert.IsTrue(Events.Has(events, RuleEventKind.GameWon));
        }

        [Test]
        public void CapturingTheQueen_EndsTheGame()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece attacker = PrepareSoldierCapture(engine, board, PieceType.Queen, PieceOwner.P2, true);

            List<RuleEvent> events = engine.ApplyMove(board.State, new Move(attacker.id, 6));

            Assert.IsTrue(board.State.gameOver);
            Assert.AreEqual(PieceOwner.P1, board.State.winner);
            Assert.IsTrue(Events.Has(events, RuleEventKind.QueenCaptured));
            Assert.IsTrue(Events.Has(events, RuleEventKind.GameWon));
            Assert.AreEqual(1, board.State.places[6].pieces.Count);
        }

        [Test]
        public void LandingOnTheKing_RecruitsIt()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece attacker = PrepareSoldierCapture(engine, board, PieceType.King, PieceOwner.None, true);

            List<RuleEvent> events = engine.ApplyMove(board.State, new Move(attacker.id, 6));

            Assert.IsTrue(Events.Has(events, RuleEventKind.KingRecruited));
            Assert.AreEqual(PieceOwner.P1, board.State.places[6].pieces[0].owner);
            Assert.AreEqual(2, board.State.places[6].pieces.Count);
            Assert.IsFalse(board.State.gameOver);
        }

        [Test]
        public void MovingASoldierIntoEnemyTerritory_ActivatesTheNeutralKing()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece king = board.Add(22, PieceType.King, PieceOwner.None, active: false);
            Piece attacker = board.Add(29, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);
            engine.EvaluateAllowedPlaces(board.State);

            engine.ApplyMove(board.State, new Move(attacker.id, 30));

            Assert.IsTrue(king.isActive);
            Assert.AreEqual(PieceOwner.P1, king.owner);
        }

        [Test]
        public void MovingAnInactiveSoldier_MakesTheNeighbourActivatable()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(4, PieceType.Soldier, PieceOwner.P1, active: false, activatable: false);
            Piece attacker = board.Add(5, PieceType.Soldier, PieceOwner.P1, active: false, activatable: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);
            engine.EvaluateAllowedPlaces(board.State);

            engine.ApplyMove(board.State, new Move(attacker.id, 6));

            Assert.IsTrue(board.State.places[4].pieces[0].canBeActivated);
            Assert.IsTrue(attacker.isActive);
        }

        [Test]
        public void IllegalMove_ProducesNoEvents()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece attacker = board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            List<RuleEvent> events = engine.ApplyMove(board.State, new Move(attacker.id, 6));

            Assert.IsEmpty(events);
            Assert.AreEqual(5, attacker.placeIndex);
        }
    }

    [TestFixture]
    public class TurnTests
    {
        static RulesEngine Engine() { return new RulesEngine(TestRuleset.Load()); }

        [Test]
        public void OrderDice_SortsAscendingByFaceValue()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.SetDice(DieFace.Zero, DieFace.Two, DieFace.Sahhku);

            engine.OrderDice(board.State);

            CollectionAssert.AreEqual(new[] { DieFace.Sahhku, DieFace.Two, DieFace.Zero }, board.State.dice);
        }

        [Test]
        public void RollAllDice_UsesTheRandomSource()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.SetDice(DieFace.Zero, DieFace.Zero, DieFace.Zero);

            engine.RollAllDice(board.State, new SequenceRandomSource(3, 0, 2));

            CollectionAssert.AreEqual(new[] { DieFace.Zero, DieFace.Sahhku, DieFace.Two }, board.State.dice);
        }

        [Test]
        public void RerollFirstDie_ReplacesOnlyTheFirstDie()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.SetDice(DieFace.Sahhku, DieFace.Three, DieFace.Two);

            engine.RerollFirstDie(board.State, new SequenceRandomSource(2));

            CollectionAssert.AreEqual(new[] { DieFace.Two, DieFace.Three, DieFace.Two }, board.State.dice);
        }

        [Test]
        public void CanReroll_RequiresSahhkuOnTheFirstDieAndAnActivePiece()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku, DieFace.Sahhku, DieFace.Sahhku);

            Assert.IsTrue(engine.CanReroll(board.State));

            board.ActiveDie(1);
            Assert.IsFalse(engine.CanReroll(board.State));

            board.ActiveDie(0);
            board.SetDice(DieFace.Three, DieFace.Sahhku, DieFace.Sahhku);
            Assert.IsFalse(engine.CanReroll(board.State));
        }

        [Test]
        public void CanReroll_IsFalseWithoutAnActivePiece()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: false, activatable: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            Assert.IsFalse(engine.CanReroll(board.State));
        }

        [Test]
        public void ApplyingAMove_AdvancesTheDieThenHandsOverTheTurn()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece attacker = board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku, DieFace.Zero, DieFace.Zero);
            engine.EvaluateAllowedPlaces(board.State);

            engine.ApplyMove(board.State, new Move(attacker.id, 6));

            Assert.AreEqual(TurnPhase.P2roll, board.State.turnPhase);
            Assert.AreEqual(0, board.State.currentActiveDie);
        }

        [Test]
        public void ApplyingTheLastDie_HandsOverTheTurn()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece attacker = board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku, DieFace.Sahhku, DieFace.Sahhku);
            board.ActiveDie(2);
            engine.EvaluateAllowedPlaces(board.State);

            engine.ApplyMove(board.State, new Move(attacker.id, 6));

            Assert.AreEqual(TurnPhase.P2roll, board.State.turnPhase);
        }

        [Test]
        public void NextPlayerTurn_ClearsAllowedPlacesAndFlipsThePlayer()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(44, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P2move);
            engine.EvaluateAllowedPlaces(board.State);

            engine.NextPlayerTurn(board.State);

            Assert.AreEqual(TurnPhase.P1roll, board.State.turnPhase);
            Assert.AreEqual(0, board.State.currentActiveDie);
            foreach (Place place in board.State.places)
            {
                foreach (Piece piece in place.pieces) Assert.IsEmpty(piece.allowedPlaces);
            }
        }
    }

    [TestFixture]
    public class RulesetJsonTests
    {
        [Test]
        public void ShippedRuleset_ParsesAndMatchesTheGame()
        {
            RuleSet rules = TestRuleset.Load();
            Assert.AreEqual(15, rules.board.width);
            Assert.AreEqual(3, rules.board.height);
            Assert.AreEqual(3, rules.dice.count);
            Assert.AreEqual(15, rules.win.soldierCapturesToWin);
            Assert.AreEqual(DieFace.Sahhku, rules.ResolveFace(rules.dice.activateFace));
            Assert.AreEqual(1, rules.FaceRules(DieFace.Sahhku).steps);
            Assert.AreEqual(3, rules.FaceRules(DieFace.Three).steps);
            Assert.AreEqual(2, rules.FaceRules(DieFace.Two).steps);
            Assert.AreEqual(0, rules.FaceRules(DieFace.Zero).steps);
        }

        [Test]
        public void Validate_RejectsAMalformedDieTable()
        {
            RuleSet rules = TestRuleset.Load();
            rules.dice.faces[1].id = "bogus";
            Assert.Throws<RuleSetException>(() => rules.Validate());
        }

        [Test]
        public void FromJson_RejectsMissingKeys()
        {
            Assert.Throws<RuleSetException>(() => RuleSetJson.FromJson("{\"id\":\"broken\"}"));
        }
    }

    [TestFixture]
    public class SimulationTests
    {
        [Test]
        public void RandomFullGame_KeepsStateConsistent()
        {
            var engine = new RulesEngine(TestRuleset.Load());
            GameState state = engine.InitGame(new EngineOptions(PieceOwner.P1, true));
            var random = new SeededRandomSource(20240607);

            int step = 0;
            for (; step < 20000 && !state.gameOver; ++step)
            {
                if (state.turnPhase == TurnPhase.P1roll || state.turnPhase == TurnPhase.P2roll)
                {
                    engine.RollAllDice(state, random);
                    engine.OrderDice(state);
                    state.currentActiveDie = 0;
                    state.turnPhase = (TurnPhase)((int)state.turnPhase + 1);

                    if (!engine.EvaluateAllowedPlaces(state)) engine.NextPlayerTurn(state);
                }
                else
                {
                    List<Move> moves = engine.GetLegalMoves(state);
                    if (moves.Count == 0)
                    {
                        engine.NextPlayerTurn(state);
                    }
                    else
                    {
                        engine.ApplyMove(state, moves[random.Next(moves.Count)]);
                    }
                }

                AssertInvariants(state);
            }

            Assert.Less(step, 20000, "the simulated game did not terminate");
        }

        static void AssertInvariants(GameState state)
        {
            int onBoard = 0;
            for (int i = 0; i < state.places.Count; ++i)
            {
                Place place = state.places[i];
                foreach (Piece piece in place.pieces)
                {
                    Assert.AreEqual(i, piece.placeIndex, "piece is not on the place that lists it");
                    Assert.That(piece.allowedPlaces, Is.All.InRange(0, state.places.Count - 1));
                    onBoard++;
                }
            }

            // 30 soldiers + 2 queens + 1 king; captures remove soldiers, a queen capture ends the game.
            Assert.LessOrEqual(onBoard + state.p1Captures + state.p2Captures, 33);
        }
    }
}
