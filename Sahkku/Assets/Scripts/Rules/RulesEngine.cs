using System.Collections.Generic;

namespace Sahkku.Rules
{
    /// <summary>
    /// The rule code, shared by the game engine and by LLM-driven NPCs. Behaviour is defined by a
    /// <see cref="RuleSet"/> loaded from JSON; this type contains only the rule primitives those
    /// definitions select. It has no Unity dependency and no hidden state, so it runs unchanged on a
    /// server that drives an NPC.
    ///
    /// Movement happens along the <see cref="TrackRules">track</see>: every piece stores the arc it
    /// sits on (<see cref="Piece.arc"/>) and its board cell follows from that. That is what makes the
    /// "8"-shaped pattern — including the middle row being walked a second time before the soldier
    /// drops back into its home row — expressible at all.
    /// </summary>
    public class RulesEngine
    {
        readonly RuleSet rules;
        readonly BoardTrack trackP1;
        readonly BoardTrack trackP2;

        public RulesEngine(RuleSet rules)
        {
            if (rules == null) throw new RuleSetException("RulesEngine requires a RuleSet.");
            this.rules = rules;
            trackP1 = BoardTrack.Build(rules.board, rules.track);
            trackP2 = trackP1.Mirror();
        }

        public RuleSet Rules { get { return rules; } }

        /// <summary>Number of arcs in one lap of a track.</summary>
        public int TrackLength { get { return trackP1.Length; } }

        /// <summary>The board cell an arc points at, on a player's track.</summary>
        public int PlaceOfArc(PieceOwner owner, int arc)
        {
            return TrackFor(owner).PlaceOf(arc);
        }

        /// <summary>The track arc a board cell maps to on a player's track (-1 when off the track).</summary>
        public int ArcOfPlace(PieceOwner owner, int placeIndex)
        {
            return TrackFor(owner).FirstArcOf(placeIndex);
        }

        /// <summary>The row a player's soldiers start on.</summary>
        public int HomeRowOf(PieceOwner owner)
        {
            return TrackFor(owner).HomeRow;
        }

        /// <summary>Creates a fresh game using the setup declared in the ruleset.</summary>
        public GameState InitGame(EngineOptions options)
        {
            var state = new GameState();
            BuildPlaces(state);

            int nextId = 0;
            AddSoldiers(state, PieceOwner.P1, rules.setup.soldiers.P1, ref nextId);
            AddSoldiers(state, PieceOwner.P2, rules.setup.soldiers.P2, ref nextId);
            AddPiece(state, rules.setup.queens.P1, PieceType.Queen, PieceOwner.P1, ref nextId);
            AddPiece(state, rules.setup.queens.P2, PieceType.Queen, PieceOwner.P2, ref nextId);
            AddPiece(state, rules.setup.king, PieceType.King, ResolveOwner(rules.setup.king.owner), ref nextId);

            VariantRules variant = options.evenOdds ? rules.variants.evenOdds : rules.variants.standard;
            ApplyVariant(state, PieceOwner.P1, variant);
            ApplyVariant(state, PieceOwner.P2, variant);

            // The board cell is derived from the arc, so normalise every piece onto its owner's track
            // (the mirror means a player-two piece's arc is not its place index).
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    piece.arc = ArcForPiece(piece);
                }
            }

            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (rules.Def(piece.type).startActivatable) piece.canBeActivated = true;
                }
            }

            for (int i = 0; i < rules.dice.count; ++i) state.dice.Add(DieFace.Zero);

            state.turnPhase = options.startingPlayer == PieceOwner.P1 ? TurnPhase.P1roll : TurnPhase.P2roll;
            return state;
        }

        /// <summary>
        /// Throws for the starting player as the ruleset prescribes (see <see cref="StartRules"/>).
        /// A host that lets the players pick instead passes the choice through
        /// <see cref="EngineOptions.startingPlayer"/> and never calls this.
        /// </summary>
        public PieceOwner ThrowForStartingPlayer(IRandomSource random)
        {
            if (random == null) throw new RuleSetException("ThrowForStartingPlayer requires a random source.");
            string activateId = rules.dice.activateFace;

            if (rules.start.mode == StartModes.MostSahhku)
            {
                int p1;
                int p2;
                do
                {
                    p1 = CountSahhku(random, activateId);
                    p2 = CountSahhku(random, activateId);
                }
                while (p1 == p2);
                return p1 > p2 ? PieceOwner.P1 : PieceOwner.P2;
            }

            PieceOwner owner = PieceOwner.P1;
            for (int attempt = 0; attempt < 10000; ++attempt)
            {
                for (int die = 0; die < rules.dice.count; ++die)
                {
                    if (rules.dice.faces[random.NextDieFaceIndex()].id == activateId) return owner;
                }
                owner = owner == PieceOwner.P1 ? PieceOwner.P2 : PieceOwner.P1;
            }
            throw new RuleSetException("ThrowForStartingPlayer never produced a sáhkku; the random source looks broken.");
        }

        int CountSahhku(IRandomSource random, string faceId)
        {
            int count = 0;
            for (int die = 0; die < rules.dice.count; ++die)
            {
                if (rules.dice.faces[random.NextDieFaceIndex()].id == faceId) count++;
            }
            return count;
        }

        /// <summary>Maps board coordinates onto the S-shaped path index; returns -1 when off the board.</summary>
        public int IndexFromCoordinates(int x, int y)
        {
            return BoardLayout.IndexFromCoordinates(rules.board.width, rules.board.height, x, y);
        }

        public void RollAllDice(GameState state, IRandomSource random)
        {
            for (int i = 0; i < state.dice.Count; ++i)
            {
                state.dice[i] = FaceFromIndex(random.NextDieFaceIndex());
            }
        }

        /// <summary>
        /// Rolls the dice, puts them into the ruleset's spending order and opens the move phase.
        /// Hands the turn straight over when the first die cannot be used, because the ruleset forbids
        /// skipping a die value.
        /// </summary>
        public void RollAndBeginTurn(GameState state, IRandomSource random)
        {
            if (state.gameOver) throw new IllegalMoveException("The game is over.");
            if (!state.IsRollPhase) throw new IllegalMoveException("It is not a roll phase.");
            RollAllDice(state, random);
            OrderDice(state);
            state.currentActiveDie = 0;
            state.turnPhase = (TurnPhase)((int)state.turnPhase + 1);
            if (!EvaluateAllowedPlaces(state)) NextPlayerTurn(state);
        }

        /// <summary>Orders the dice into the spending order declared by the ruleset.</summary>
        public void OrderDice(GameState state)
        {
            state.dice.Sort(delegate(DieFace a, DieFace b)
            {
                return rules.UseOrderOf(a).CompareTo(rules.UseOrderOf(b));
            });
        }

        /// <summary>
        /// Throws the die currently up for spending again. Only legal while the ruleset allows
        /// re-rolling (here: while a sáhkku face is up and no die has been spent yet).
        /// </summary>
        public void RerollFirstDie(GameState state, IRandomSource random)
        {
            if (!CanReroll(state)) throw new IllegalMoveException("Re-rolling is not allowed right now.");
            state.dice[state.currentActiveDie] = FaceFromIndex(random.NextDieFaceIndex());
        }

        public bool CanReroll(GameState state)
        {
            if (state.gameOver || state.IsRollPhase) return false;
            if (rules.dice.reroll.beforeUsingAnyDie && state.currentActiveDie != 0) return false;
            return CanRerollFace(CurrentFace(state));
        }

        /// <summary>Recomputes <see cref="Piece.allowedPlaces"/> for the current player; returns whether any piece can move.</summary>
        public bool EvaluateAllowedPlaces(GameState state)
        {
            if (state.gameOver || state.IsRollPhase)
            {
                ClearAllowedPlaces(state);
                return false;
            }

            bool anyAllowedPlaces = false;
            PieceOwner current = state.CurrentPlayer;
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (piece.owner == current)
                    {
                        piece.allowedPlaces = GetAllowedPlaces(state, piece);
                        if (piece.IsSelectable()) anyAllowedPlaces = true;
                    }
                    else
                    {
                        piece.allowedPlaces.Clear();
                    }
                }
            }
            return anyAllowedPlaces;
        }

        public void ClearAllowedPlaces(GameState state)
        {
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    piece.allowedPlaces.Clear();
                }
            }
        }

        /// <summary>Board cells the piece may move to with the die currently being spent.</summary>
        public List<int> GetAllowedPlaces(GameState state, Piece piece)
        {
            var allowed = new List<int>();
            BoardTrack track = TrackFor(piece.owner);
            foreach (int arc in GetAllowedArcs(state, piece))
            {
                int place = track.PlaceOf(arc);
                if (!allowed.Contains(place)) allowed.Add(place);
            }
            return allowed;
        }

        /// <summary>
        /// The track arcs a piece may move to with the die currently being spent. This is the
        /// authoritative form of "what is legal"; <see cref="GetAllowedPlaces"/> projects it onto
        /// board cells for the UI. Two arcs share a middle-row cell, so the arc is what makes a move
        /// unambiguous.
        /// </summary>
        public List<int> GetAllowedArcs(GameState state, Piece piece)
        {
            var allowed = new List<int>();
            if (piece == null) return allowed;

            PieceRules def = rules.Def(piece.type);
            BoardTrack track = TrackFor(piece.owner);
            int steps = def.movesScaleWithDie ? StepsFor(CurrentFace(state)) : 1;
            if (steps == 0) return allowed;

            bool canAct = piece.isActive
                || (piece.canBeActivated && CurrentFace(state) == ResolveFace(rules.dice.activateFace));
            if (!canAct) return allowed;

            if (def.Has(MovePatterns.Forward)) TryAddArc(state, track, piece, piece.arc + steps, allowed);
            if (def.Has(MovePatterns.Backward)) TryAddArc(state, track, piece, piece.arc - steps, allowed);
            if (def.Has(MovePatterns.Vertical))
            {
                Place origin = state.places[piece.placeIndex];
                TryAddCrossing(state, track, piece, origin.x, origin.y + steps, allowed);
                TryAddCrossing(state, track, piece, origin.x, origin.y - steps, allowed);
            }
            return allowed;
        }

        public List<Piece> GetPotentialPieces(GameState state)
        {
            var potential = new List<Piece>();
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (piece.IsSelectable()) potential.Add(piece);
                }
            }
            return potential;
        }

        /// <summary>
        /// Every legal action for the current player according to the cached
        /// <see cref="Piece.allowedPlaces"/>. Call <see cref="EvaluateAllowedPlaces"/> first, or use
        /// <see cref="LegalMoves"/>, which always computes from scratch.
        /// </summary>
        public List<Move> GetLegalMoves(GameState state)
        {
            var moves = new List<Move>();
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (!piece.IsSelectable()) continue;
                    foreach (int target in piece.allowedPlaces)
                    {
                        moves.Add(new Move(piece.id, target));
                    }
                }
            }
            return moves;
        }

        /// <summary>Every legal action for the current player, computed from scratch. The flat, LLM-facing view.</summary>
        public List<Move> LegalMoves(GameState state)
        {
            var moves = new List<Move>();
            if (state.gameOver || state.IsRollPhase) return moves;

            PieceOwner current = state.CurrentPlayer;
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (piece.owner != current) continue;
                    foreach (int target in GetAllowedPlaces(state, piece))
                    {
                        moves.Add(new Move(piece.id, target));
                    }
                }
            }
            return moves;
        }

        /// <summary>True when <paramref name="move"/> is a legal action in the current state.</summary>
        public bool IsLegalMove(GameState state, Move move)
        {
            if (state == null) return false;
            if (state.gameOver || state.IsRollPhase) return false;

            Piece piece = FindPiece(state, move.pieceId);
            if (piece == null || piece.owner != state.CurrentPlayer) return false;
            if (move.targetPlaceIndex < 0 || move.targetPlaceIndex >= state.places.Count) return false;
            return ResolveTargetArc(state, piece, move.targetPlaceIndex) >= 0;
        }

        public Piece FindPiece(GameState state, int pieceId)
        {
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (piece.id == pieceId) return piece;
                }
            }
            return null;
        }

        /// <summary>
        /// Applies a move and returns the presentation events it produced. Throws
        /// <see cref="IllegalMoveException"/> when the move is not legal, so no caller — UI, replay
        /// file, network message or LLM — can drive the game into an illegal state. Use
        /// <see cref="TryApplyMove"/> for the non-throwing form.
        /// </summary>
        public List<RuleEvent> ApplyMove(GameState state, Move move)
        {
            List<RuleEvent> events;
            if (!TryApplyMove(state, move, out events))
            {
                throw new IllegalMoveException("Illegal move " + move + " for " + state.CurrentPlayer + " in phase " + state.turnPhase + ".");
            }
            return events;
        }

        /// <summary>Non-throwing <see cref="ApplyMove"/>: false (and no events) for an illegal move.</summary>
        public bool TryApplyMove(GameState state, Move move, out List<RuleEvent> events)
        {
            events = new List<RuleEvent>();
            if (state == null || state.gameOver || state.IsRollPhase) return false;

            Piece piece = FindPiece(state, move.pieceId);
            if (piece == null || piece.owner != state.CurrentPlayer) return false;
            if (move.targetPlaceIndex < 0 || move.targetPlaceIndex >= state.places.Count) return false;

            int newArc = ResolveTargetArc(state, piece, move.targetPlaceIndex);
            if (newArc < 0) return false;

            PieceOwner current = state.CurrentPlayer;
            Place target = state.places[move.targetPlaceIndex];

            ApplyLanding(state, piece, target, current, events);

            // The ruleset may make moving an inactive piece unlock the next one in the queue.
            SoldierActivationRule activation = rules.activation.onMoveInactivePiece;
            if (!piece.isActive && rules.Def(piece.type).queuesNextOnActivation)
            {
                UnlockNextInQueue(state, piece, current, activation.unlockOffset);
            }

            state.places[piece.placeIndex].pieces.Remove(piece);
            piece.isActive = true;
            piece.placeIndex = move.targetPlaceIndex;
            piece.arc = TrackFor(piece.owner).Normalize(newArc);
            target.pieces.Add(piece);

            // A soldier reaching the opponent's home row recruits the neutral king.
            if (rules.Def(piece.type).recruitsKingOnEnemyHomeRow)
            {
                PieceOwner opponent = current == PieceOwner.P1 ? PieceOwner.P2 : PieceOwner.P1;
                BoardTrack track = TrackFor(current);
                if (track.RowOfLeg(track.LegOf(newArc)) == TrackFor(opponent).HomeRow)
                {
                    RecruitNeutralKing(state, current, events);
                }
            }

            if (state.gameOver) return true;

            state.currentActiveDie++;
            if (state.currentActiveDie >= state.dice.Count)
            {
                NextPlayerTurn(state);
            }
            else if (!EvaluateAllowedPlaces(state))
            {
                NextPlayerTurn(state);
            }
            return true;
        }

        /// <summary>
        /// Checks the invariants that must hold in every state. Returns a list of problems; an empty
        /// list means the state is sound. Used by the tests and available to hosts as a debug aid.
        /// </summary>
        public List<string> ValidateState(GameState state)
        {
            var problems = new List<string>();
            if (state == null)
            {
                problems.Add("state is null");
                return problems;
            }

            var seen = new HashSet<int>();
            int soldiers = 0;
            for (int i = 0; i < state.places.Count; ++i)
            {
                Place place = state.places[i];
                bool p1 = false;
                bool p2 = false;
                bool royal = false;
                int sharedWithRoyal = 0;

                foreach (Piece piece in place.pieces)
                {
                    if (!seen.Add(piece.id)) problems.Add("piece " + piece.id + " appears on the board more than once");
                    if (piece.placeIndex != i) problems.Add("piece " + piece.id + " claims place " + piece.placeIndex + " but sits on place " + i);
                    if (piece.owner == PieceOwner.P1) p1 = true;
                    if (piece.owner == PieceOwner.P2) p2 = true;
                    if (piece.type != PieceType.Soldier)
                    {
                        royal = true;
                        sharedWithRoyal = 0;
                    }
                    else
                    {
                        soldiers++;
                        sharedWithRoyal++;
                    }

                    int arcPlace = TrackFor(piece.owner).PlaceOf(piece.arc);
                    if (arcPlace != piece.placeIndex)
                        problems.Add("piece " + piece.id + " has arc " + piece.arc + " which points at place " + arcPlace + ", not " + piece.placeIndex);

                    foreach (int allowed in piece.allowedPlaces)
                    {
                        if (allowed < 0 || allowed >= state.places.Count) problems.Add("piece " + piece.id + " allows off-board place " + allowed);
                    }
                }

                if (p1 && p2) problems.Add("place " + i + " holds pieces of both players");
                if (royal && place.pieces.Count > 1 && !SharesLineWithItsRecruiter(place))
                    problems.Add("place " + i + " makes a royal piece share its line");
            }

            if (soldiers + state.p1Captures + state.p2Captures != 2 * rules.board.width)
                problems.Add("soldiers are not conserved: " + soldiers + " on board plus " + state.p1Captures + " and " +
                             state.p2Captures + " captures is not " + (2 * rules.board.width));

            if (state.gameOver && state.winner == PieceOwner.None) problems.Add("the game is over without a winner");
            if (!state.gameOver && state.winner != PieceOwner.None) problems.Add("a winner is set but the game is not over");
            if (state.currentActiveDie < 0 || state.currentActiveDie >= state.dice.Count)
                problems.Add("currentActiveDie " + state.currentActiveDie + " is out of range");
            if (!rules.win.opponentSoldiersExhausted && state.gameOver) problems.Add("the game ended although the ruleset declares no win condition");
            return problems;
        }

        public void NextPlayerTurn(GameState state)
        {
            ClearAllowedPlaces(state);
            state.currentActiveDie = 0;
            state.turnPhase = state.CurrentPlayer == PieceOwner.P2 ? TurnPhase.P1roll : TurnPhase.P2roll;
        }

        // ------------------------------------------------------------------ internals

        static bool SharesLineWithItsRecruiter(Place place)
        {
            // The ruleset's one exception: a piece that moved onto the king to recruit it stays on
            // the king's line until one of the two moves away.
            if (place.pieces.Count != 2) return false;
            Piece king = place.pieces[0];
            return king.type == PieceType.King && place.pieces[1].owner == king.owner;
        }

        void ApplyLanding(GameState state, Piece piece, Place target, PieceOwner current, List<RuleEvent> events)
        {
            var indicesToRemove = new List<int>();
            bool endedByCapture = false;

            for (int i = 0; i < target.pieces.Count; ++i)
            {
                Piece other = target.pieces[i];
                if (other.owner == current) continue;

                PieceRules otherRules = rules.Def(other.type);
                if (otherRules.recruitedWhenLanded)
                {
                    other.owner = current;
                    other.arc = ArcForPiece(other);
                    events.Add(MakeEvent(RuleEventKind.KingRecruited, other.type, current));
                }
                else if (otherRules.capturable)
                {
                    indicesToRemove.Add(i);
                    if (otherRules.landingEndsGame) endedByCapture = true;
                    events.Add(MakeEvent(
                        otherRules.landingEndsGame ? RuleEventKind.QueenCaptured : RuleEventKind.SoldierCaptured,
                        other.type, current));

                    // The capture counters score soldiers taken, and the host shows them as such.
                    if (!otherRules.landingEndsGame)
                    {
                        if (current == PieceOwner.P1) state.p1Captures++;
                        else state.p2Captures++;
                    }
                }
            }

            if (indicesToRemove.Count == 0)
            {
                events.Add(MakeEvent(RuleEventKind.PieceMoved, piece.type, current));
            }

            for (int i = indicesToRemove.Count - 1; i >= 0; --i)
            {
                target.pieces.RemoveAt(indicesToRemove[i]);
            }

            if (indicesToRemove.Count > 0) CheckWinAfterCapture(state, current, endedByCapture, events);
        }

        void CheckWinAfterCapture(GameState state, PieceOwner current, bool endedByCapture, List<RuleEvent> events)
        {
            PieceOwner opponent = current == PieceOwner.P1 ? PieceOwner.P2 : PieceOwner.P1;

            if (endedByCapture)
            {
                EndGame(state, current, WinReason.QueenCaptured, PieceType.Queen, events);
                return;
            }

            if (rules.win.opponentSoldiersExhausted && CountSoldiers(state, opponent) == 0)
            {
                EndGame(state, current, WinReason.OpponentSoldiersExhausted, PieceType.Soldier, events);
            }
        }

        static void EndGame(GameState state, PieceOwner winner, WinReason reason, PieceType lastPieceType, List<RuleEvent> events)
        {
            state.gameOver = true;
            state.winner = winner;
            state.winReason = reason;
            events.Add(MakeEvent(RuleEventKind.GameWon, lastPieceType, winner, winner));
        }

        void RecruitNeutralKing(GameState state, PieceOwner current, List<RuleEvent> events)
        {
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (piece.type != PieceType.King || piece.isActive) continue;
                    piece.isActive = true;
                    piece.owner = current;
                    piece.arc = ArcForPiece(piece);
                    events.Add(MakeEvent(RuleEventKind.KingRecruited, piece.type, current));
                }
            }
        }

        /// <summary>Makes the piece one home-row line further back than the mover's source activatable.</summary>
        void UnlockNextInQueue(GameState state, Piece mover, PieceOwner current, int unlockOffset)
        {
            BoardTrack track = TrackFor(current);
            int sourceOffset = track.ArcOfPlaceInLeg(0, mover.placeIndex);
            if (sourceOffset < 0) return;

            int targetOffset = track.OffsetOf(sourceOffset) + unlockOffset;
            if (targetOffset < 0 || targetOffset >= rules.board.width) return;

            int neighbour = track.PlaceOf(targetOffset);
            if (neighbour < 0 || neighbour >= state.places.Count) return;
            if (state.places[neighbour].pieces.Count == 0) return;
            state.places[neighbour].pieces[0].canBeActivated = true;
        }

        int CountSoldiers(GameState state, PieceOwner owner)
        {
            int count = 0;
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (piece.owner == owner && piece.type == PieceType.Soldier) count++;
                }
            }
            return count;
        }

        /// <summary>The arc a piece's current cell maps to on its owner's track (used when ownership changes).</summary>
        int ArcForPiece(Piece piece)
        {
            int arc = TrackFor(piece.owner).FirstArcOf(piece.placeIndex);
            if (arc < 0) throw new RuleSetException("Piece " + piece.id + " stands on place " + piece.placeIndex + ", which is not on the track.");
            return arc;
        }

        /// <summary>
        /// The arc a move onto <paramref name="targetPlace"/> departs from, or -1 when the piece cannot
        /// reach that cell with the die currently up for spending. Two arcs point at the same
        /// middle-row cell, so the arc — not the cell — is what makes a move unambiguous.
        /// </summary>
        int ResolveTargetArc(GameState state, Piece piece, int targetPlace)
        {
            BoardTrack track = TrackFor(piece.owner);
            foreach (int arc in GetAllowedArcs(state, piece))
            {
                if (track.PlaceOf(arc) == targetPlace) return arc;
            }
            return -1;
        }

        void TryAddArc(GameState state, BoardTrack track, Piece piece, int arc, List<int> allowed)
        {
            int normalized = track.Normalize(arc);
            int place = track.PlaceOf(normalized);
            if (place < 0) return;
            if (!CanLandOn(state, piece, place)) return;
            if (!allowed.Contains(normalized)) allowed.Add(normalized);
        }

        void TryAddCrossing(GameState state, BoardTrack track, Piece piece, int x, int row, List<int> allowed)
        {
            if (x < 0 || x >= rules.board.width) return;
            if (row < 0 || row >= rules.board.height) return;

            int place = BoardLayout.IndexFromCoordinates(rules.board.width, rules.board.height, x, row);
            if (place < 0) return;

            int arc = track.CrossingArc(piece.arc, row, place);
            if (arc < 0) return;
            if (!CanLandOn(state, piece, place)) return;
            if (!allowed.Contains(arc)) allowed.Add(arc);
        }

        bool CanLandOn(GameState state, Piece mover, int place)
        {
            Place target = state.places[place];
            if (target.pieces.Count == 0) return true;

            Piece top = target.pieces[0];
            if (!top.isActive) return rules.inactive.enterable;

            bool ownUnit = top.owner == mover.owner;
            if (rules.Def(top.type).blocksOwnLanding && ownUnit) return false;
            if (rules.Def(mover.type).cannotLandOnOwnUnits && ownUnit) return false;
            return true;
        }

        DieFace CurrentFace(GameState state)
        {
            if (state.dice.Count == 0) throw new RuleSetException("The ruleset must declare at least one die.");
            if (state.currentActiveDie < 0 || state.currentActiveDie >= state.dice.Count)
                throw new IllegalMoveException("The active die index is out of range.");
            return state.dice[state.currentActiveDie];
        }

        int StepsFor(DieFace face)
        {
            return rules.FaceRules(face).steps;
        }

        DieFace FaceFromIndex(int index)
        {
            if (index < 0 || index >= rules.dice.faces.Length)
                throw new RuleSetException("Die face index " + index + " is out of range.");
            return ResolveFace(rules.dice.faces[index].id);
        }

        bool CanRerollFace(DieFace face)
        {
            string id = RuleSet.FaceId(face);
            for (int i = 0; i < rules.dice.reroll.faces.Length; ++i)
            {
                if (rules.dice.reroll.faces[i] == id) return true;
            }
            return false;
        }

        DieFace ResolveFace(string id)
        {
            return rules.ResolveFace(id);
        }

        BoardTrack TrackFor(PieceOwner owner)
        {
            return owner == PieceOwner.P2 ? trackP2 : trackP1;
        }

        void BuildPlaces(GameState state)
        {
            // Snake order, so that place index == IndexFromCoordinates(place.x, place.y).
            for (int y = 0; y < rules.board.height; ++y)
            {
                for (int i = 0; i < rules.board.width; ++i)
                {
                    int x = (y % 2 == 1) ? (rules.board.width - 1 - i) : i;
                    state.places.Add(new Place(x, y));
                }
            }
        }

        void AddSoldiers(GameState state, PieceOwner owner, RowPlacement placement, ref int nextId)
        {
            for (int x = 0; x < rules.board.width; ++x)
            {
                int index = BoardLayout.IndexFromCoordinates(rules.board.width, rules.board.height, x, placement.row);
                state.places[index].pieces.Add(new Piece(nextId++, index, PieceType.Soldier, owner));
            }
        }

        void AddPiece(GameState state, Placement placement, PieceType type, PieceOwner owner, ref int nextId)
        {
            int index = IndexFromCoordinates(placement.x, placement.row);
            if (index < 0) throw new RuleSetException("Piece '" + type + "' is placed off the board.");
            state.places[index].pieces.Add(new Piece(nextId++, index, type, owner));
        }

        /// <summary>
        /// Marks the leading soldiers of a player's home row active. The activation queue runs from
        /// the foremost soldier — the one whose forward move leaves the home row — towards the rear,
        /// so "the first N soldiers are loose" fully describes the starting position.
        /// </summary>
        void ApplyVariant(GameState state, PieceOwner owner, VariantRules variant)
        {
            BoardTrack track = TrackFor(owner);
            int homeRow = track.HomeRow;

            var soldiers = new List<Piece>();
            var arcs = new List<int>();
            foreach (Place place in state.places)
            {
                if (place.y != homeRow) continue;
                foreach (Piece piece in place.pieces)
                {
                    if (piece.owner != owner || piece.type != PieceType.Soldier) continue;
                    int arc = track.ArcOfPlaceInLeg(0, piece.placeIndex);
                    if (arc < 0) continue;
                    soldiers.Add(piece);
                    arcs.Add(arc);
                }
            }

            // Foremost first: the soldier furthest along the home-row leg.
            for (int i = 0; i < soldiers.Count; ++i)
            {
                for (int j = i + 1; j < soldiers.Count; ++j)
                {
                    if (arcs[j] > arcs[i])
                    {
                        int arc = arcs[i]; arcs[i] = arcs[j]; arcs[j] = arc;
                        Piece piece = soldiers[i]; soldiers[i] = soldiers[j]; soldiers[j] = piece;
                    }
                }
            }

            for (int i = 0; i < soldiers.Count; ++i)
            {
                if (i < variant.soldiersActive) soldiers[i].isActive = true;
                else if (i == variant.soldiersActive) soldiers[i].canBeActivated = true;
            }
        }

        static PieceOwner ResolveOwner(string owner)
        {
            if (owner == "P1") return PieceOwner.P1;
            if (owner == "P2") return PieceOwner.P2;
            if (owner == "neutral" || owner == "none") return PieceOwner.None;
            throw new RuleSetException("Unknown piece owner '" + owner + "'.");
        }

        static RuleEvent MakeEvent(RuleEventKind kind, PieceType type, PieceOwner owner)
        {
            return new RuleEvent { kind = kind, pieceType = type, owner = owner };
        }

        static RuleEvent MakeEvent(RuleEventKind kind, PieceType type, PieceOwner owner, PieceOwner winner)
        {
            return new RuleEvent { kind = kind, pieceType = type, owner = owner, winner = winner };
        }
    }
}
