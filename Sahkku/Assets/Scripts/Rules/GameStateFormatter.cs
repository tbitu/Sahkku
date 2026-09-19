using System;
using System.Collections.Generic;
using System.Text;

namespace Sahkku.Rules
{
    /// <summary>
    /// Pure presentation of a <see cref="GameState"/>: deterministic ASCII board grids and a
    /// structured prompt context for LLM agents and CLI runners. A formatter is a pure function of
    /// the state — same state in, same string out; it reads only from <c>GameState.places</c>,
    /// <c>GameState.dice</c> and the pieces' stored arcs, with no engine calls, no randomness and
    /// no side effects.
    /// </summary>
    public static class GameStateFormatter
    {
        /// <summary>
        /// Renders the board as a grid of lines, one per board row: top line is the highest y (in
        /// the shipped ruleset that is player two's home row), bottom line is y=0 (player one).
        /// Each cell shows the pieces on that place: '.' when empty, otherwise one token per piece —
        /// 'S'/'Q'/'K' plus the owner digit (0 neutral, 1 P1, 2 P2) — with a count for stacks of
        /// identical tokens (e.g. 'S1(x2)') and '+' between distinct tokens in stable order.
        /// </summary>
        public static string FormatBoardAscii(GameState state)
        {
            if (state == null || state.places.Count == 0) throw new ArgumentNullException("state");

            int width = 0;
            int height = 0;
            foreach (Place place in state.places)
            {
                if (place.x + 1 > width) width = place.x + 1;
                if (place.y + 1 > height) height = place.y + 1;
            }

            var cells = new string[height * width];
            int cellWidth = 3;
            foreach (Place place in state.places)
            {
                if (place.x < 0 || place.x >= width || place.y < 0 || place.y >= height) continue;
                string content = CellContent(place);
                cells[place.y * width + place.x] = content;
                if (content.Length > cellWidth) cellWidth = content.Length;
            }

            var sb = new StringBuilder();
            for (int y = height - 1; y >= 0; --y)
            {
                if (y < height - 1) sb.Append('\n');
                sb.Append("y=").Append(y).Append(" |");
                for (int x = 0; x < width; ++x)
                {
                    string content = cells[y * width + x];
                    if (content == null) content = "."; // a place the state does not declare renders empty
                    if (x < width - 1 && content.Length < cellWidth)
                        content = Padded(content, cellWidth);
                    sb.Append(' ').Append(content);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// A structured Markdown context for an agent that must decide the current player's action:
        /// turn and phase, game-over status, capture scores, the dice in spending order with the
        /// active die marked, the board grid, the pieces under the current player's control (with
        /// coordinates and activation status), and every legal move as a numbered list. The move
        /// numbers are the indices into <paramref name="legalMoves"/>, which is what an agent returns
        /// from <c>IPlayerAgent.DecideMoveAsync</c>. Deterministic for a given state and move list.
        /// </summary>
        public static string FormatPromptContext(GameState state, IReadOnlyList<Move> legalMoves)
        {
            if (state == null || state.places.Count == 0) throw new ArgumentNullException("state");
            if (legalMoves == null) throw new ArgumentNullException("legalMoves");

            PieceOwner current = state.CurrentPlayer;
            var sb = new StringBuilder();

            sb.Append("# Sahkku decision context\n");
            sb.Append("- Turn: ").Append(OwnerName(current))
              .Append(", phase: ").Append(state.IsRollPhase ? "roll" : "move").Append('\n');
            if (state.gameOver)
            {
                sb.Append("- Game over: yes, winner ").Append(OwnerName(state.winner))
                  .Append(" (").Append(state.winReason).Append(")\n");
            }
            else
            {
                sb.Append("- Game over: no\n");
            }
            sb.Append("- Captures: P1: ").Append(state.p1Captures)
              .Append(", P2: ").Append(state.p2Captures).Append('\n');

            sb.Append("\n## Dice in spending order\n");
            if (state.IsRollPhase)
            {
                sb.Append("- Not yet thrown this turn.\n");
            }
            else
            {
                for (int i = 0; i < state.dice.Count; ++i)
                {
                    sb.Append(i == state.currentActiveDie ? "- [active] " : "- ")
                      .Append(DieFaceName(state.dice[i])).Append(" (die ").Append(i).Append(")\n");
                }
            }

            sb.Append("\n## Board\n").Append(FormatBoardAscii(state)).Append('\n');

            sb.Append("\n## Your pieces (").Append(OwnerName(current)).Append(")\n");
            bool anyPiece = false;
            foreach (Place place in state.places)
            {
                foreach (Piece piece in place.pieces)
                {
                    if (piece.owner != current) continue;
                    anyPiece = true;
                    sb.Append("- ").Append(PieceName(piece.type)).Append(" #").Append(piece.id)
                      .Append(" at (x=").Append(place.x).Append(", y=").Append(place.y).Append("), place ")
                      .Append(piece.placeIndex).Append(", arc ").Append(piece.arc);
                    if (piece.isActive) sb.Append(": active");
                    else if (piece.canBeActivated) sb.Append(": inactive, may be activated");
                    else sb.Append(": inactive");
                    sb.Append('\n');
                }
            }
            if (!anyPiece) sb.Append("- (none)\n");

            sb.Append("\n## Legal moves\n");
            if (legalMoves.Count == 0)
            {
                sb.Append("(no legal moves)\n");
            }
            for (int i = 0; i < legalMoves.Count; ++i)
            {
                sb.Append(MoveDescription(state, i, legalMoves[i])).Append('\n');
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ internals

        /// <summary>The cell text for one place: '.', a single token, or a stack summary.</summary>
        static string CellContent(Place place)
        {
            if (place.pieces.Count == 0) return ".";
            if (place.pieces.Count == 1) return TokenOf(place.pieces[0]);

            // Stable order for mixed stacks: by piece type, then owner.
            var ordered = new List<Piece>(place.pieces);
            ordered.Sort(delegate(Piece a, Piece b)
            {
                int byType = ((int)a.type).CompareTo((int)b.type);
                if (byType != 0) return byType;
                return ((int)a.owner).CompareTo((int)b.owner);
            });

            var sb = new StringBuilder();
            for (int i = 0; i < ordered.Count;)
            {
                int j = i + 1;
                while (j < ordered.Count && TokenOf(ordered[j]) == TokenOf(ordered[i])) ++j;
                if (sb.Length > 0) sb.Append('+');
                sb.Append(TokenOf(ordered[i]));
                if (j - i > 1) sb.Append("(x").Append(j - i).Append(')');
                i = j;
            }
            return sb.ToString();
        }

        /// <summary>'S'/'Q'/'K' plus the owner digit: S1 is player one's soldier, K0 the neutral king.</summary>
        static string TokenOf(Piece piece)
        {
            char letter = piece.type == PieceType.Soldier ? 'S' : (piece.type == PieceType.Queen ? 'Q' : 'K');
            return new string(letter, 1) + ((int)piece.owner).ToString();
        }

        static string MoveDescription(GameState state, int index, Move move)
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(index).Append("] ");

            Piece piece;
            Place source;
            if (!LocatePiece(state, move.pieceId, out piece, out source))
                return sb.Append("(unknown piece)").ToString();
            if (move.targetPlaceIndex < 0 || move.targetPlaceIndex >= state.places.Count)
                return sb.Append(PieceName(piece.type)).Append(" #").Append(piece.id).Append(" -> (off-board)").ToString();

            Place target = state.places[move.targetPlaceIndex];
            sb.Append(PieceName(piece.type)).Append(" #").Append(piece.id)
              .Append(" at line ").Append(source.x).Append(", row ").Append(source.y).Append(" (place ")
              .Append(piece.placeIndex).Append(") -> line ").Append(target.x).Append(", row ").Append(target.y)
              .Append(" (place ").Append(move.targetPlaceIndex).Append(')');
            if (target.y != source.y) sb.Append(", crosses to row ").Append(target.y);
            return sb.ToString();
        }

        static string Padded(string value, int width)
        {
            var sb = new StringBuilder(value);
            for (int i = value.Length; i < width; ++i) sb.Append(' ');
            return sb.ToString();
        }

        /// <summary>Finds a piece by id together with the place it currently sits on.</summary>
        static bool LocatePiece(GameState state, int pieceId, out Piece piece, out Place place)
        {
            piece = null;
            place = null;
            foreach (Place candidate in state.places)
            {
                foreach (Piece found in candidate.pieces)
                {
                    if (found.id != pieceId) continue;
                    piece = found;
                    place = candidate;
                    return true;
                }
            }
            return false;
        }

        static string OwnerName(PieceOwner owner)
        {
            switch (owner)
            {
                case PieceOwner.P1: return "P1";
                case PieceOwner.P2: return "P2";
                default: return "neutral";
            }
        }

        static string PieceName(PieceType type)
        {
            switch (type)
            {
                case PieceType.Soldier: return "Soldier";
                case PieceType.Queen: return "Queen";
                default: return "King";
            }
        }

        static string DieFaceName(DieFace face)
        {
            switch (face)
            {
                case DieFace.Sahhku: return "X";
                case DieFace.Three: return "III";
                case DieFace.Two: return "II";
                default: return "-";
            }
        }
    }
}
