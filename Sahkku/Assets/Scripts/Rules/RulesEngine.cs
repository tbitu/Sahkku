using System.Collections.Generic;

namespace Sahkku.Rules
{
    /// <summary>
    /// The rule code, shared by the game engine and (later) by LLM-driven NPCs. Behaviour is defined
    /// by a <see cref="RuleSet"/> loaded from JSON; this type contains only the rule primitives those
    /// definitions select. It has no Unity dependency and no hidden state, so it can run on the
    /// server side for NPC/LLM tooling.
    /// </summary>
    public class RulesEngine
    {
        readonly RuleSet rules;

        public RulesEngine(RuleSet rules)
        {
            if (rules == null) throw new RuleSetException("RulesEngine requires a RuleSet.");
            this.rules = rules;
        }

        public RuleSet Rules { get { return rules; } }

        /// <summary>Creates a fresh game using the standard default setup for the given options.</summary>
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
            ApplyCoordinates(state, variant.active, MarkActive);
            ApplyCoordinates(state, variant.activatable, MarkActivatable);

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

        /// <summary>Maps board coordinates onto the S-shaped path index; returns -1 when off the board.</summary>
        public int IndexFromCoordinates(int x, int y)
        {
            if (x < 0 || x >= rules.board.width || y < 0 || y >= rules.board.height) return -1;
            int i = (y % 2 == 1) ? (rules.board.width - 1 - x) : x;
            return y * rules.board.width + i;
        }

        public void RollAllDice(GameState state, IRandomSource random)
        {
            for (int i = 0; i < state.dice.Count; ++i)
            {
                state.dice[i] = FaceFromIndex(random.NextDieFaceIndex());
            }
        }

        /// <summary>Orders the dice ascending by face value, exactly as the original <c>dice.Sort()</c>.</summary>
        public void OrderDice(GameState state)
        {
            state.dice.Sort();
        }

        public void RerollFirstDie(GameState state, IRandomSource random)
        {
            state.dice[0] = FaceFromIndex(random.NextDieFaceIndex());
        }

        public bool CanReroll(GameState state)
        {
            if (rules.dice.rerollRequiresFirstDie && state.currentActiveDie != 0) return false;
            if (CurrentFace(state) != rules.ResolveFace(rules.dice.rerollFace)) return false;

            PieceOwner current = state.CurrentPlayer;
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (piece.owner == current && piece.isActive) return true;
                }
            }
            return false;
        }

        /// <summary>Recomputes <see cref="Piece.allowedPlaces"/> for the current player; returns whether any piece can move.</summary>
        public bool EvaluateAllowedPlaces(GameState state)
        {
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

        public List<int> GetAllowedPlaces(GameState state, Piece piece)
        {
            var allowed = new List<int>();
            PieceRules def = rules.Def(piece.type);
            int steps = def.movesScaleWithDie ? StepsFor(CurrentFace(state)) : 1;

            if (steps == 0) return allowed;

            bool canAct = piece.isActive
                || (piece.canBeActivated && CurrentFace(state) == rules.ResolveFace(rules.dice.activateFace));
            if (!canAct) return allowed;

            int direction = (state.CurrentPlayer == PieceOwner.P1 ? 1 : -1) * steps;

            if (def.Has(MovePatterns.Forward)) TryAddAllowedPlace(state, piece.placeIndex + direction, allowed, piece);
            if (def.Has(MovePatterns.Backward)) TryAddAllowedPlace(state, piece.placeIndex - direction, allowed, piece);
            if (def.Has(MovePatterns.Vertical))
            {
                Place origin = state.places[piece.placeIndex];
                TryAddAllowedPlace(state, IndexFromCoordinates(origin.x, origin.y + steps), allowed, piece);
                TryAddAllowedPlace(state, IndexFromCoordinates(origin.x, origin.y - steps), allowed, piece);
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

        /// <summary>Every legal action for the current player; the flat view an LLM NPC would consume.</summary>
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
        /// Applies a move and returns the presentation events it produced. The caller is expected to
        /// have called <see cref="EvaluateAllowedPlaces"/> first (as the original UI flow does).
        /// </summary>
        public List<RuleEvent> ApplyMove(GameState state, Move move)
        {
            var events = new List<RuleEvent>();
            Piece piece = FindPiece(state, move.pieceId);
            if (piece == null || piece.allowedPlaces.Count == 0) return events;

            PieceOwner current = state.CurrentPlayer;
            Place target = state.places[move.targetPlaceIndex];
            var indicesToRemove = new List<int>();

            // Branch order mirrors the original implementation: soldier, king, then queen.
            for (int i = 0; i < target.pieces.Count; ++i)
            {
                Piece other = target.pieces[i];
                PieceRules otherRules = rules.Def(other.type);

                if (other.type == PieceType.Soldier && other.owner != current && otherRules.capturable)
                {
                    indicesToRemove.Add(i);
                    events.Add(MakeEvent(RuleEventKind.SoldierCaptured, other.type, current));

                    if (current == PieceOwner.P1) state.p1Captures++;
                    else state.p2Captures++;

                    int captures = current == PieceOwner.P1 ? state.p1Captures : state.p2Captures;
                    if (captures == rules.win.soldierCapturesToWin)
                    {
                        state.gameOver = true;
                        state.winner = current;
                        events.Add(MakeEvent(RuleEventKind.GameWon, other.type, current, current));
                    }
                }
                else if (other.type == PieceType.King && otherRules.recruitedWhenLanded)
                {
                    other.owner = current;
                    events.Add(MakeEvent(RuleEventKind.KingRecruited, other.type, current));
                }
                else if (other.type == PieceType.Queen && otherRules.capturable)
                {
                    indicesToRemove.Add(i);
                    state.gameOver = true;
                    state.winner = current;
                    events.Add(MakeEvent(RuleEventKind.QueenCaptured, other.type, current));
                    events.Add(MakeEvent(RuleEventKind.GameWon, other.type, current, current));
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

            // Moving an inactive soldier makes the neighbouring soldier activatable.
            SoldierActivationRule activation = rules.activation.onMoveInactiveSoldier;
            if (activation != null && !piece.isActive && piece.type == PieceType.Soldier)
            {
                int sign = (activation.perPlayerSign && current == PieceOwner.P2) ? -1 : 1;
                int neighbour = piece.placeIndex + sign * activation.offset;
                if (neighbour >= 0 && neighbour < state.places.Count && state.places[neighbour].pieces.Count > 0)
                {
                    state.places[neighbour].pieces[0].canBeActivated = true;
                }
            }

            state.places[piece.placeIndex].pieces.Remove(piece);
            piece.isActive = true;
            piece.placeIndex = move.targetPlaceIndex;
            target.pieces.Add(piece);

            // A soldier reaching the enemy's home territory recruits the neutral king.
            if (piece.type == PieceType.Soldier && rules.Def(PieceType.King).recruitedOnEnemyTerritory)
            {
                bool enemyTerritory = current == PieceOwner.P1
                    ? move.targetPlaceIndex >= state.places.Count - rules.board.width
                    : move.targetPlaceIndex < rules.board.width;
                if (enemyTerritory) ActivateKing(state, current);
            }

            if (state.gameOver) return events;

            state.currentActiveDie++;
            if (state.currentActiveDie >= state.dice.Count)
            {
                NextPlayerTurn(state);
            }
            else if (!EvaluateAllowedPlaces(state))
            {
                NextPlayerTurn(state);
            }
            return events;
        }

        public void NextPlayerTurn(GameState state)
        {
            ClearAllowedPlaces(state);
            state.currentActiveDie = 0;
            state.turnPhase = state.CurrentPlayer == PieceOwner.P2 ? TurnPhase.P1roll : TurnPhase.P2roll;
        }

        DieFace CurrentFace(GameState state)
        {
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
            return (DieFace)index;
        }

        void TryAddAllowedPlace(GameState state, int indexTarget, List<int> allowed, Piece mover)
        {
            if (indexTarget < 0 || indexTarget > state.places.Count - 1) return;

            Place target = state.places[indexTarget];
            if (target.pieces.Count == 0)
            {
                allowed.Add(indexTarget);
                return;
            }

            Piece top = target.pieces[0];
            bool ownUnit = top.owner == state.CurrentPlayer;
            if (top.isActive
                && !(rules.Def(top.type).blocksOwnLanding && ownUnit)
                && !(rules.Def(mover.type).cannotLandOnOwnUnits && ownUnit))
            {
                allowed.Add(indexTarget);
            }
        }

        void ActivateKing(GameState state, PieceOwner current)
        {
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (piece.type == PieceType.King && !piece.isActive)
                    {
                        state.places[piece.placeIndex].pieces[0].isActive = true;
                        state.places[piece.placeIndex].pieces[0].owner = current;
                    }
                }
            }
        }

        void BuildPlaces(GameState state)
        {
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
            for (int i = 0; i < rules.board.width; ++i)
            {
                int index = placement.row * rules.board.width + i;
                state.places[index].pieces.Add(new Piece(nextId++, index, PieceType.Soldier, owner));
            }
        }

        void AddPiece(GameState state, Placement placement, PieceType type, PieceOwner owner, ref int nextId)
        {
            int index = IndexFromCoordinates(placement.x, placement.row);
            if (index < 0) throw new RuleSetException("Piece '" + type + "' is placed off the board.");
            state.places[index].pieces.Add(new Piece(nextId++, index, type, owner));
        }

        void ApplyCoordinates(GameState state, CoordRules[] coordinates, System.Action<Piece> apply)
        {
            if (coordinates == null) return;
            foreach (CoordRules coordinate in coordinates)
            {
                int index = IndexFromCoordinates(coordinate.x, coordinate.y);
                if (index < 0) throw new RuleSetException("Variant coordinate (" + coordinate.x + ", " + coordinate.y + ") is off the board.");
                if (state.places[index].pieces.Count > 0) apply(state.places[index].pieces[0]);
            }
        }

        static void MarkActive(Piece piece) { piece.isActive = true; }
        static void MarkActivatable(Piece piece) { piece.canBeActivated = true; }

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
