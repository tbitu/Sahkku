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
            piece.arc = Engine.ArcOfPlace(owner, placeIndex);
            piece.canBeActivated = activatable;
            State.places[placeIndex].pieces.Add(piece);
            return piece;
        }

        /// <summary>Places a piece on a specific arc, i.e. on a specific pass over the board.</summary>
        public Piece AddOnArc(int arc, PieceType type, PieceOwner owner, bool active = false, bool activatable = false)
        {
            int place = Engine.PlaceOfArc(owner, arc);
            var piece = new Piece(nextId++, place, type, owner, active);
            piece.arc = arc;
            piece.canBeActivated = activatable;
            State.places[place].pieces.Add(piece);
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
            RulesEngine engine = Engine();
            GameState state = engine.InitGame(new EngineOptions(PieceOwner.P1, false));

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

            Assert.IsEmpty(engine.ValidateState(state), "the initial position must satisfy the invariants");
        }

        [Test]
        public void InitGame_EvenOdds_MarksTheThreeForemostSoldiersLoose()
        {
            RulesEngine engine = Engine();
            GameState state = engine.InitGame(new EngineOptions(PieceOwner.P1, true));

            // The activation queue runs from the foremost soldier backwards, so "three soldiers are
            // taken loose" means the three foremost ones are active and the fourth awaits its X.
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

            Assert.AreEqual(3, engine.Rules.variants.evenOdds.soldiersActive);
            Assert.AreEqual(0, engine.Rules.variants.standard.soldiersActive);
            Assert.IsEmpty(engine.ValidateState(state));
        }

        [Test]
        public void InitGame_RespectsStartingPlayer()
        {
            GameState state = Engine().InitGame(new EngineOptions(PieceOwner.P2, false));
            Assert.AreEqual(TurnPhase.P2roll, state.turnPhase);
            Assert.AreEqual(PieceOwner.P2, state.CurrentPlayer);
        }

        [Test]
        public void ThrowForStartingPlayer_FirstSahhkuStarts()
        {
            RulesEngine engine = Engine();

            // P1 throws blank-blank-blank (3,3,3), P2 throws a sáhkku on the first die.
            var random = new SequenceRandomSource(3, 3, 3, 0, 1, 1, 2);
            Assert.AreEqual(PieceOwner.P2, engine.ThrowForStartingPlayer(random));
        }
    }

    [TestFixture]
    public class TrackTests
    {
        static RulesEngine Engine() { return new RulesEngine(TestRuleset.Load()); }

        [Test]
        public void Track_WalksTheFigureOfEightAndReturnsToTheHomeRow()
        {
            RulesEngine engine = Engine();
            Assert.AreEqual(60, engine.TrackLength);

            // Home row right, middle row left, enemy row right, middle row left, then home again.
            int[] expected = new int[60];
            for (int i = 0; i < 15; ++i) expected[i] = i;              // row 0, x 0..14
            for (int i = 0; i < 15; ++i) expected[15 + i] = 15 + i;    // row 1, x 14..0
            for (int i = 0; i < 15; ++i) expected[30 + i] = 30 + i;    // row 2, x 0..14
            for (int i = 0; i < 15; ++i) expected[45 + i] = 15 + i;    // row 1 again, x 14..0

            for (int arc = 0; arc < 60; ++arc)
            {
                Assert.AreEqual(expected[arc], engine.PlaceOfArc(PieceOwner.P1, arc), "P1 arc " + arc);
            }
            Assert.AreEqual(engine.PlaceOfArc(PieceOwner.P1, 0), engine.PlaceOfArc(PieceOwner.P1, 60), "the lap closes");
        }

        [Test]
        public void Track_IsTheBoardMirrorForTheSecondPlayer()
        {
            RulesEngine engine = Engine();
            for (int arc = 0; arc < engine.TrackLength; ++arc)
            {
                int p1 = engine.PlaceOfArc(PieceOwner.P1, arc);
                int p2 = engine.PlaceOfArc(PieceOwner.P2, arc);

                int x, y;
                BoardLayout.CoordinatesFromIndex(15, p1, out x, out y);
                Assert.AreEqual(engine.IndexFromCoordinates(14 - x, 2 - y), p2, "arc " + arc);
            }

            Assert.AreEqual(0, engine.HomeRowOf(PieceOwner.P1));
            Assert.AreEqual(2, engine.HomeRowOf(PieceOwner.P2));
            Assert.AreEqual(14, engine.PlaceOfArc(PieceOwner.P1, 14));
            Assert.AreEqual(30, engine.PlaceOfArc(PieceOwner.P2, 14));
        }

        [Test]
        public void SecondPassOverTheMiddleRow_DescendsIntoTheHomeRow()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);

            // The same board cell (x=0, y=1) is reached twice per lap. On the way out the soldier
            // steps up to the enemy row; on the way back it steps down into its home row.
            Piece outbound = board.AddOnArc(29, PieceType.Soldier, PieceOwner.P1, active: true);
            board.AddOnArc(0, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            Assert.AreEqual(29, outbound.placeIndex);
            CollectionAssert.AreEqual(new[] { 30 }, engine.GetAllowedPlaces(board.State, outbound));

            var board2 = new Board(engine);
            Piece returning = board2.AddOnArc(59, PieceType.Soldier, PieceOwner.P1, active: true);
            board2.AddOnArc(0, PieceType.Soldier, PieceOwner.P1, active: true);
            board2.Phase(TurnPhase.P1move);
            board2.SetDice(DieFace.Sahhku);

            Assert.AreEqual(29, returning.placeIndex, "arc 59 also stands on x=0, y=1");
            CollectionAssert.AreEqual(new[] { 0 }, engine.GetAllowedPlaces(board2.State, returning));
        }

        [Test]
        public void Soldier_AtTheEndOfTheEnemyRow_ContinuesOnTheMiddleRow()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece piece = board.Add(44, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            // 44 = (14, 2): instead of falling off the board it turns onto the middle row.
            CollectionAssert.AreEqual(new[] { 15 }, engine.GetAllowedPlaces(board.State, piece));

            board.SetDice(DieFace.Three);
            CollectionAssert.AreEqual(new[] { 17 }, engine.GetAllowedPlaces(board.State, piece));
        }

        [Test]
        public void PlayerTwo_AtTheEndOfItsEnemyRow_ContinuesOnTheMiddleRow()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine, PieceOwner.P2);
            Piece piece = board.Add(0, PieceType.Soldier, PieceOwner.P2, active: true);
            board.Phase(TurnPhase.P2move);
            board.SetDice(DieFace.Sahhku);

            // 0 = (0, 0) is the end of player two's enemy row.
            CollectionAssert.AreEqual(new[] { 29 }, engine.GetAllowedPlaces(board.State, piece));
        }

        [Test]
        public void Soldier_FullLap_ReturnsToItsStartingCell()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece soldier = board.AddOnArc(1, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);

            var legs = new List<int> { soldier.arc / 15 };
            // Twenty three-line moves are sixty lines: exactly one lap of the figure of eight.
            for (int move = 0; move < 20; ++move)
            {
                board.SetDice(DieFace.Three);
                engine.EvaluateAllowedPlaces(board.State);
                List<int> allowed = engine.GetAllowedPlaces(board.State, soldier);
                Assert.AreEqual(1, allowed.Count, "move " + move);
                engine.ApplyMove(board.State, new Move(soldier.id, allowed[0]));
                board.Phase(TurnPhase.P1move);

                if (legs[legs.Count - 1] != soldier.arc / 15) legs.Add(soldier.arc / 15);
            }

            // Home row -> middle row -> enemy row -> middle row -> back into the home row.
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 0 }, legs);
            Assert.AreEqual(1, soldier.arc);
            Assert.AreEqual(engine.PlaceOfArc(PieceOwner.P1, 1), soldier.placeIndex);
        }

        [Test]
        public void King_CrossingAtTheBendIsAlsoAStraightStep()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece king = board.Add(14, PieceType.King, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            // At the bend the forward step and the vertical crossing lead to the same cell, so it is
            // offered once; the king may also step straight back along its home row.
            Assert.AreEqual(15, engine.PlaceOfArc(PieceOwner.P1, king.arc + 1));
            CollectionAssert.AreEqual(new[] { 15, 13 }, engine.GetAllowedPlaces(board.State, king));
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
        public void King_CrossesRowsOnlyWithTheExactDieValue()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            // 22 = the Castle, middle row (7,1).
            board.Add(22, PieceType.King, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            Piece king = board.State.places[22].pieces[0];

            // From the middle row, crossing to a home row needs an X (one line).
            board.SetDice(DieFace.Three);
            CollectionAssert.DoesNotContain(engine.GetAllowedPlaces(board.State, king), 37);
            CollectionAssert.DoesNotContain(engine.GetAllowedPlaces(board.State, king), 7);

            board.SetDice(DieFace.Sahhku);
            CollectionAssert.Contains(engine.GetAllowedPlaces(board.State, king), 37);
            CollectionAssert.Contains(engine.GetAllowedPlaces(board.State, king), 7);
        }

        [Test]
        public void King_FromTheHomeRowCrossesTheMiddleRowOrJumpsToTheEnemyRow()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece king = board.Add(7, PieceType.King, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);

            board.SetDice(DieFace.Sahhku);
            CollectionAssert.Contains(engine.GetAllowedPlaces(board.State, king), 22); // (7,1)
            board.SetDice(DieFace.Two);
            // II crosses straight over the middle row onto the enemy row.
            CollectionAssert.Contains(engine.GetAllowedPlaces(board.State, king), 37); // (7,2)
            CollectionAssert.DoesNotContain(engine.GetAllowedPlaces(board.State, king), 22);
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
        public void InactiveRule_ComesFromTheRuleset()
        {
            RuleSet rules = TestRuleset.Load();
            rules.inactive.enterable = true; // a variant where unactivated pieces may be attacked
            var engine = new RulesEngine(rules);

            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Add(6, PieceType.Soldier, PieceOwner.P2, active: false);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            CollectionAssert.AreEqual(new[] { 6 }, engine.GetAllowedPlaces(board.State, board.State.places[5].pieces[0]));
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

        [Test]
        public void LegalMoves_DoesNotNeedAnEarlierEvaluateAllowedPlaces()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            List<Move> moves = engine.LegalMoves(board.State);
            Assert.AreEqual(1, moves.Count);
            Assert.AreEqual(6, moves[0].targetPlaceIndex);
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
            board.State.p1Captures = 0;

            List<RuleEvent> events = engine.ApplyMove(board.State, new Move(attacker.id, 6));

            Assert.IsTrue(Events.Has(events, RuleEventKind.SoldierCaptured));
            Assert.IsFalse(Events.Has(events, RuleEventKind.PieceMoved));
            Assert.AreEqual(1, board.State.p1Captures);
            Assert.AreEqual(0, board.State.p2Captures);
            Assert.AreEqual(6, attacker.placeIndex);
            Assert.AreEqual(1, board.State.places[6].pieces.Count);
        }

        [Test]
        public void CapturingEveryEnemySoldier_EndsTheGame()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            Piece attacker = PrepareSoldierCapture(engine, board, PieceType.Soldier, PieceOwner.P2, true);
            board.State.p1Captures = 14; // the other fourteen were taken earlier

            List<RuleEvent> events = engine.ApplyMove(board.State, new Move(attacker.id, 6));

            Assert.AreEqual(15, board.State.p1Captures);
            Assert.IsTrue(board.State.gameOver);
            Assert.AreEqual(PieceOwner.P1, board.State.winner);
            Assert.AreEqual(WinReason.OpponentSoldiersExhausted, board.State.winReason);
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
            Assert.AreEqual(WinReason.QueenCaptured, board.State.winReason);
            Assert.IsTrue(Events.Has(events, RuleEventKind.QueenCaptured));
            Assert.IsTrue(Events.Has(events, RuleEventKind.GameWon));
            Assert.AreEqual(1, board.State.places[6].pieces.Count);
        }

        [Test]
        public void LosingTheQueenOnlyEndsTheGameWhenTheRulesetSaysSo()
        {
            RuleSet rules = TestRuleset.Load();
            rules.pieces.queen.landingEndsGame = false; // e.g. a variant where the queen does not decide the game
            var engine = new RulesEngine(rules);

            var board = new Board(engine);
            Piece attacker = board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Add(6, PieceType.Queen, PieceOwner.P2, active: true);
            board.Add(7, PieceType.Soldier, PieceOwner.P2, active: true); // the opponent still has a soldier
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);
            engine.EvaluateAllowedPlaces(board.State);

            List<RuleEvent> events = engine.ApplyMove(board.State, new Move(attacker.id, 6));

            Assert.AreEqual(1, board.State.places[6].pieces.Count);
            Assert.AreEqual(PieceType.Soldier, board.State.places[6].pieces[0].type, "the queen is gone, the soldier stands there");
            Assert.IsFalse(board.State.gameOver);
            Assert.AreEqual(WinReason.None, board.State.winReason);
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
        public void KingRecruitmentComesFromTheRuleset()
        {
            RuleSet rules = TestRuleset.Load();
            rules.pieces.soldier.recruitsKingOnEnemyHomeRow = false;
            var engine = new RulesEngine(rules);

            var board = new Board(engine);
            Piece king = board.Add(22, PieceType.King, PieceOwner.None, active: false);
            Piece attacker = board.Add(29, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);
            engine.EvaluateAllowedPlaces(board.State);

            engine.ApplyMove(board.State, new Move(attacker.id, 30));

            Assert.IsFalse(king.isActive);
            Assert.AreEqual(PieceOwner.None, king.owner);
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
        public void MovingAnInactiveQueen_DoesNotUnlockASoldier()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(4, PieceType.Soldier, PieceOwner.P1, active: false, activatable: false);
            Piece queen = board.Add(5, PieceType.Queen, PieceOwner.P1, active: false, activatable: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);
            engine.EvaluateAllowedPlaces(board.State);

            engine.ApplyMove(board.State, new Move(queen.id, 6));

            Assert.IsFalse(board.State.places[4].pieces[0].canBeActivated);
        }
    }

    [TestFixture]
    public class MoveValidationTests
    {
        static RulesEngine Engine() { return new RulesEngine(TestRuleset.Load()); }

        static Board Ready(RulesEngine engine)
        {
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Add(6, PieceType.Soldier, PieceOwner.P2, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);
            engine.EvaluateAllowedPlaces(board.State);
            return board;
        }

        [Test]
        public void ApplyMove_RejectsATargetThatIsNotLegal()
        {
            RulesEngine engine = Engine();
            Board board = Ready(engine);
            Piece attacker = board.State.places[5].pieces[0];

            Assert.Throws<IllegalMoveException>(() => engine.ApplyMove(board.State, new Move(attacker.id, 30)));
            Assert.AreEqual(5, attacker.placeIndex, "the piece must not have moved");
        }

        [Test]
        public void ApplyMove_RejectsAnOffBoardTarget()
        {
            RulesEngine engine = Engine();
            Board board = Ready(engine);
            Piece attacker = board.State.places[5].pieces[0];

            Assert.Throws<IllegalMoveException>(() => engine.ApplyMove(board.State, new Move(attacker.id, 9999)));
            Assert.Throws<IllegalMoveException>(() => engine.ApplyMove(board.State, new Move(attacker.id, -1)));
        }

        [Test]
        public void ApplyMove_RejectsTheOpponentsPiece()
        {
            RulesEngine engine = Engine();
            Board board = Ready(engine);
            Piece victim = board.State.places[6].pieces[0];

            Assert.Throws<IllegalMoveException>(() => engine.ApplyMove(board.State, new Move(victim.id, 6)));
        }

        [Test]
        public void ApplyMove_IsRejectedAfterTheGameHasEnded()
        {
            RulesEngine engine = Engine();
            Board board = Ready(engine);
            Piece attacker = board.State.places[5].pieces[0];
            board.State.gameOver = true;
            board.State.winner = PieceOwner.P1;

            Assert.Throws<IllegalMoveException>(() => engine.ApplyMove(board.State, new Move(attacker.id, 6)));
        }

        [Test]
        public void TryApplyMove_ReportsIllegalMovesWithoutThrowing()
        {
            RulesEngine engine = Engine();
            Board board = Ready(engine);
            Piece attacker = board.State.places[5].pieces[0];

            List<RuleEvent> events;
            Assert.IsFalse(engine.TryApplyMove(board.State, new Move(attacker.id, 30), out events));
            Assert.IsEmpty(events);
            Assert.IsTrue(engine.TryApplyMove(board.State, new Move(attacker.id, 6), out events));
            Assert.IsNotEmpty(events);
        }

        [Test]
        public void Clone_LetsASearchTryAMoveWithoutTouchingTheLiveGame()
        {
            RulesEngine engine = Engine();
            Board board = Ready(engine);
            Piece attacker = board.State.places[5].pieces[0];

            GameState copy = board.State.Clone();
            Assert.IsTrue(engine.TryApplyMove(copy, new Move(attacker.id, 6), out var _));

            Assert.AreEqual(6, engine.FindPiece(copy, attacker.id).placeIndex, "the copy moved");
            Assert.AreEqual(5, attacker.placeIndex, "the live game did not");
            Assert.IsTrue(copy.gameOver, "the copy took the opponent's last soldier");
            Assert.IsFalse(board.State.gameOver, "the live game is untouched");
        }

        [Test]
        public void EvaluateAllowedPlaces_IsFalseWhileTheDiceAreStillToBeThrown()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1roll);
            board.SetDice(DieFace.Sahhku);

            Assert.IsFalse(engine.EvaluateAllowedPlaces(board.State));
            Assert.IsEmpty(engine.LegalMoves(board.State));
        }
    }

    [TestFixture]
    public class TurnTests
    {
        static RulesEngine Engine() { return new RulesEngine(TestRuleset.Load()); }

        [Test]
        public void OrderDice_UsesTheRulesetSpendingOrder()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.SetDice(DieFace.Zero, DieFace.Two, DieFace.Sahhku);

            engine.OrderDice(board.State);

            CollectionAssert.AreEqual(new[] { DieFace.Sahhku, DieFace.Two, DieFace.Zero }, board.State.dice);
        }

        [Test]
        public void OrderDice_SpendsThreeBeforeTwoBeforeBlank()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.SetDice(DieFace.Zero, DieFace.Two, DieFace.Three);

            engine.OrderDice(board.State);

            CollectionAssert.AreEqual(new[] { DieFace.Three, DieFace.Two, DieFace.Zero }, board.State.dice);
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
        public void RollAndBeginTurn_OrdersDiceAndOpensTheMovePhase()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.SetDice(DieFace.Zero, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1roll);

            engine.RollAndBeginTurn(board.State, new SequenceRandomSource(3, 0, 2));

            CollectionAssert.AreEqual(new[] { DieFace.Sahhku, DieFace.Two, DieFace.Zero }, board.State.dice);
            Assert.AreEqual(TurnPhase.P1move, board.State.turnPhase);
            Assert.AreEqual(0, board.State.currentActiveDie);
        }

        [Test]
        public void RollAndBeginTurn_HandsOverWhenNoDieCanBeUsed()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            // Only an inactive soldier that cannot be activated: no die value helps.
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: false, activatable: false);
            board.SetDice(DieFace.Zero, DieFace.Zero, DieFace.Zero);
            board.Phase(TurnPhase.P1roll);

            engine.RollAndBeginTurn(board.State, new SequenceRandomSource(3, 3, 3));

            Assert.AreEqual(TurnPhase.P2roll, board.State.turnPhase);
        }

        [Test]
        public void RerollFirstDie_ReplacesOnlyTheFirstDie()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku, DieFace.Three, DieFace.Two);

            engine.RerollFirstDie(board.State, new SequenceRandomSource(2));

            CollectionAssert.AreEqual(new[] { DieFace.Two, DieFace.Three, DieFace.Two }, board.State.dice);
        }

        [Test]
        public void Reroll_DoesNotRequireAnAlreadyActivePiece()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            // The rules let a player re-roll the X dice; nothing says a piece must be loose already.
            board.Add(5, PieceType.Soldier, PieceOwner.P1, active: false, activatable: true);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Sahhku);

            Assert.IsTrue(engine.CanReroll(board.State));
        }

        [Test]
        public void Reroll_IsOnlyOfferedBeforeAnyDieHasBeenSpent()
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
        public void Reroll_RejectedWhenItIsNotAllowed()
        {
            RulesEngine engine = Engine();
            var board = new Board(engine);
            board.Phase(TurnPhase.P1move);
            board.SetDice(DieFace.Three, DieFace.Three, DieFace.Three);

            Assert.Throws<IllegalMoveException>(() => engine.RerollFirstDie(board.State, new SequenceRandomSource(0)));
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
        public void ShippedRuleset_DescribesTheVuonnamarkanGame()
        {
            RuleSet rules = TestRuleset.Load();
            Assert.AreEqual(15, rules.board.width);
            Assert.AreEqual(3, rules.board.height);
            Assert.AreEqual(3, rules.dice.count);
            Assert.AreEqual(DieFace.Sahhku, rules.ResolveFace(rules.dice.activateFace));

            Assert.AreEqual(4, rules.track.legs.Length);
            Assert.AreEqual("right", rules.track.legs[0].direction);
            Assert.AreEqual("left", rules.track.legs[1].direction);
            Assert.AreEqual(2, rules.track.legs[2].row);

            Assert.AreEqual(0, rules.UseOrderOf(DieFace.Sahhku));
            Assert.AreEqual(1, rules.UseOrderOf(DieFace.Three));
            Assert.AreEqual(2, rules.UseOrderOf(DieFace.Two));
            Assert.AreEqual(3, rules.UseOrderOf(DieFace.Zero));

            Assert.AreEqual(1, rules.FaceRules(DieFace.Sahhku).steps);
            Assert.AreEqual(3, rules.FaceRules(DieFace.Three).steps);
            Assert.AreEqual(2, rules.FaceRules(DieFace.Two).steps);
            Assert.AreEqual(0, rules.FaceRules(DieFace.Zero).steps);

            Assert.AreEqual("sahhku", rules.dice.reroll.faces[0]);
            Assert.IsFalse(rules.inactive.enterable);
            Assert.IsTrue(rules.win.opponentSoldiersExhausted);
            Assert.IsTrue(rules.pieces.soldier.recruitsKingOnEnemyHomeRow);
            Assert.IsTrue(rules.pieces.queen.landingEndsGame);
            Assert.IsFalse(rules.pieces.king.capturable);
            Assert.AreEqual(-1, rules.activation.onMoveInactivePiece.unlockOffset);
            Assert.AreEqual(StartModes.FirstSahhku, rules.start.mode);
        }

        [Test]
        public void NoSchemaFieldIsLeftUnread()
        {
            // Guards against rules that look authoritative but that the engine never consults: every
            // key in the JSON must be part of the documented schema below.
            string json = TestRuleset.Json();
            string[] schemaKeys =
            {
                "track", "legs", "row", "direction", "moves", "movesScaleWithDie", "startActivatable",
                "queuesNextOnActivation", "blocksOwnLanding", "cannotLandOnOwnUnits", "capturable",
                "recruitedWhenLanded", "landingEndsGame", "recruitsKingOnEnemyHomeRow", "count",
                "activateFace", "useOrder", "reroll", "faces", "beforeUsingAnyDie", "steps",
                "onMoveInactivePiece", "unlockOffset", "enterable", "mode", "soldiers", "queens", "king",
                "placement", "x", "y", "owner", "soldiersActive", "opponentSoldiersExhausted",
                "width", "height", "layout", "id", "version", "P1", "P2", "standard", "evenOdds",
                "pieces", "soldier", "queen", "king", "three", "two", "zero", "neutral", "board",
                "dice", "activation", "inactive", "start", "setup", "variants", "win"
            };

            foreach (string key in Keys(json))
            {
                bool known = false;
                foreach (string candidate in schemaKeys)
                {
                    if (candidate == key) { known = true; break; }
                }
                Assert.IsTrue(known, "ruleset key '" + key + "' is not part of the documented schema");
            }
        }

        static List<string> Keys(string json)
        {
            var keys = new List<string>();
            bool inString = false;
            var current = new System.Text.StringBuilder();
            for (int i = 0; i < json.Length; ++i)
            {
                char c = json[i];
                if (!inString)
                {
                    if (c == '"') { inString = true; current.Length = 0; }
                    continue;
                }
                if (c == '\\') { ++i; continue; }
                if (c != '"') { current.Append(c); continue; }

                inString = false;
                int j = i + 1;
                while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                if (j < json.Length && json[j] == ':') keys.Add(current.ToString());
            }
            return keys;
        }

        [Test]
        public void Validate_RejectsAMalformedDieTable()
        {
            RuleSet rules = TestRuleset.Load();
            rules.dice.faces[1].id = "bogus";
            Assert.Throws<RuleSetException>(() => rules.Validate());
        }

        [Test]
        public void Validate_RejectsASpendingOrderThatSkipsAFace()
        {
            RuleSet rules = TestRuleset.Load();
            rules.dice.useOrder = new[] { "sahhku", "three", "two", "two" };
            Assert.Throws<RuleSetException>(() => rules.Validate());
        }

        [Test]
        public void Validate_RejectsATrackWhoseLegsDoNotJoin()
        {
            RuleSet rules = TestRuleset.Load();
            rules.track.legs[1].direction = "right"; // no longer meets leg 0 at x=14
            Assert.Throws<RuleSetException>(() => rules.Validate());
        }

        [Test]
        public void Validate_RejectsATrackThatNeverBends()
        {
            RuleSet rules = TestRuleset.Load();
            rules.track.legs[1].row = 0;
            Assert.Throws<RuleSetException>(() => rules.Validate());
        }

        [Test]
        public void Validate_RejectsOverlappingSetup()
        {
            RuleSet rules = TestRuleset.Load();
            rules.setup.king.x = 11; // on top of P1's queen
            Assert.Throws<RuleSetException>(() => rules.Validate());
        }

        [Test]
        public void Validate_RejectsARulesetWithoutAWayToWin()
        {
            RuleSet rules = TestRuleset.Load();
            rules.win.opponentSoldiersExhausted = false;
            rules.pieces.queen.landingEndsGame = false;
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
                Assert.IsEmpty(engine.ValidateState(state), "invariants broke at step " + step);

                if (state.IsRollPhase)
                {
                    engine.RollAndBeginTurn(state, random);
                }
                else
                {
                    List<Move> moves = engine.LegalMoves(state);
                    if (moves.Count == 0)
                    {
                        engine.NextPlayerTurn(state);
                    }
                    else
                    {
                        engine.ApplyMove(state, moves[random.Next(moves.Count)]);
                    }
                }
            }

            Assert.Less(step, 20000, "the simulated game did not terminate");
            Assert.IsTrue(state.gameOver);
            Assert.AreNotEqual(PieceOwner.None, state.winner);
            Assert.IsEmpty(engine.ValidateState(state));
        }

        [Test]
        public void RandomGame_KeepsTheBoardInsideTheTrack()
        {
            // A longer random game with the other starting variant: every piece must stay on the arc
            // it claims, which only holds if the figure-of-eight wrap is applied consistently.
            var engine = new RulesEngine(TestRuleset.Load());
            GameState state = engine.InitGame(new EngineOptions(PieceOwner.P1, false));
            var random = new SeededRandomSource(987654321);

            for (int step = 0; step < 20000 && !state.gameOver; ++step)
            {
                if (state.IsRollPhase) engine.RollAndBeginTurn(state, random);
                else
                {
                    List<Move> moves = engine.LegalMoves(state);
                    if (moves.Count == 0) engine.NextPlayerTurn(state);
                    else engine.ApplyMove(state, moves[random.Next(moves.Count)]);
                }

                Assert.IsEmpty(engine.ValidateState(state), "invariants broke at step " + step);
            }

            Assert.IsTrue(state.gameOver, "the game should have been decided");
        }
    }
}
