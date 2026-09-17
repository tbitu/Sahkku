using System;
using System.Collections.Generic;

namespace Sahkku.Rules
{
    /// <summary>Kind of piece; values mirror the original GameLogic enum ordering.</summary>
    public enum PieceType
    {
        Soldier = 0,
        King = 1,
        Queen = 2
    }

    public enum PieceOwner
    {
        None = 0,
        P1 = 1,
        P2 = 2
    }

    /// <summary>
    /// Identity of a sáhkku die face (birccu). Purely an identity: the ruleset decides how many
    /// steps each face is worth and in which order faces must be spent.
    /// </summary>
    public enum DieFace
    {
        Sahhku = 0,
        Three = 1,
        Two = 2,
        Zero = 3
    }

    public enum TurnPhase
    {
        P1roll = 0,
        P1move = 1,
        P2roll = 2,
        P2move = 3
    }

    /// <summary>Why the game ended. Lets a host pick the right message without re-deriving rules.</summary>
    public enum WinReason
    {
        None = 0,

        /// <summary>The loser has no soldiers left on the board.</summary>
        OpponentSoldiersExhausted = 1,

        /// <summary>The loser's queen was captured.</summary>
        QueenCaptured = 2
    }

    /// <summary>
    /// Thrown when a caller asks the engine to apply an action that is not legal in the current
    /// state. Distinct from <see cref="RuleSetException"/>, which means the *ruleset* is broken.
    /// </summary>
    public sealed class IllegalMoveException : Exception
    {
        public IllegalMoveException(string message) : base(message) { }
    }

    /// <summary>A single playing piece. It lives on exactly one <see cref="Place"/>.</summary>
    public class Piece
    {
        /// <summary>Stable identity used to refer to a piece from a <see cref="Move"/>.</summary>
        public int id;

        /// <summary>Index into <see cref="GameState.places"/>; the board square the piece occupies.</summary>
        public int placeIndex;

        /// <summary>
        /// Position along its owner's track (see <see cref="TrackRules"/>), in <c>[0, TrackLength)</c>.
        /// <see cref="placeIndex"/> is derived from it, so the two can never disagree. Two arcs can
        /// map onto the same middle-row place, which is exactly why this cannot be derived back
        /// from <see cref="placeIndex"/> and has to be stored.
        /// </summary>
        public int arc;

        public PieceType type;
        public PieceOwner owner;
        public bool isActive;
        public bool canBeActivated;
        public List<int> allowedPlaces = new List<int>();

        public Piece(int id, int placeIndex, PieceType type, PieceOwner owner, bool isActive = false)
        {
            this.id = id;
            this.placeIndex = placeIndex;
            this.arc = placeIndex;
            this.type = type;
            this.owner = owner;
            this.isActive = isActive;
        }

        public bool IsSelectable()
        {
            return allowedPlaces.Count > 0;
        }
    }

    /// <summary>A board point (place) and the pieces currently on it.</summary>
    public class Place
    {
        public int x;
        public int y;
        public List<Piece> pieces = new List<Piece>();

        public Place(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    /// <summary>Mutable game state. Contains no Unity types, so it can be produced/consumed off-engine.</summary>
    public class GameState
    {
        public List<Place> places = new List<Place>();
        public List<DieFace> dice = new List<DieFace>();
        public TurnPhase turnPhase;
        public int currentActiveDie;
        public bool gameOver;
        public PieceOwner winner = PieceOwner.None;
        public WinReason winReason = WinReason.None;
        public int p1Captures;
        public int p2Captures;

        /// <summary>The player whose turn it is, derived from <see cref="turnPhase"/>.</summary>
        public PieceOwner CurrentPlayer
        {
            get { return (int)turnPhase < 2 ? PieceOwner.P1 : PieceOwner.P2; }
        }

        /// <summary>True while the current player still has to roll (or reroll) dice.</summary>
        public bool IsRollPhase
        {
            get { return turnPhase == TurnPhase.P1roll || turnPhase == TurnPhase.P2roll; }
        }

        /// <summary>Deep copy, for search (LLM NPCs) and for tests that need to try a move twice.</summary>
        public GameState Clone()
        {
            var copy = new GameState
            {
                turnPhase = turnPhase,
                currentActiveDie = currentActiveDie,
                gameOver = gameOver,
                winner = winner,
                winReason = winReason,
                p1Captures = p1Captures,
                p2Captures = p2Captures
            };
            copy.dice.AddRange(dice);
            foreach (Place place in places)
            {
                var placeCopy = new Place(place.x, place.y);
                foreach (Piece piece in place.pieces)
                {
                    var pieceCopy = new Piece(piece.id, piece.placeIndex, piece.type, piece.owner, piece.isActive)
                    {
                        arc = piece.arc,
                        canBeActivated = piece.canBeActivated
                    };
                    pieceCopy.allowedPlaces.AddRange(piece.allowedPlaces);
                    placeCopy.pieces.Add(pieceCopy);
                }
                copy.places.Add(placeCopy);
            }
            return copy;
        }
    }

    /// <summary>A proposed or legal action: move the piece with <see cref="pieceId"/> onto <see cref="targetPlaceIndex"/>.</summary>
    public struct Move
    {
        public int pieceId;
        public int targetPlaceIndex;

        public Move(int pieceId, int targetPlaceIndex)
        {
            this.pieceId = pieceId;
            this.targetPlaceIndex = targetPlaceIndex;
        }

        public override string ToString()
        {
            return "piece " + pieceId + " -> place " + targetPlaceIndex;
        }
    }

    public enum RuleEventKind
    {
        PieceMoved,
        SoldierCaptured,
        KingRecruited,
        QueenCaptured,
        GameWon
    }

    /// <summary>
    /// A side effect of an applied action, emitted in the exact order the original implementation
    /// triggered its audio, so the host can play sounds / visuals.
    /// </summary>
    public struct RuleEvent
    {
        public RuleEventKind kind;
        public PieceType pieceType;
        public PieceOwner owner;
        public PieceOwner winner;
    }

    /// <summary>Injectable randomness so the engine stays deterministic and Unity-free.</summary>
    public interface IRandomSource
    {
        /// <summary>Returns a die face index in [0, 4).</summary>
        int NextDieFaceIndex();
    }

    /// <summary>
    /// Chooses an action for a player. The current AI implements this randomly; a future LLM-driven
    /// NPC will provide another implementation backed by the same ruleset.
    /// </summary>
    public interface IActionSelector
    {
        bool TryChooseAction(GameState state, IReadOnlyList<Move> legalMoves, out Move move);
    }

    /// <summary>Options supplied by the host (menu/settings) when starting a game.</summary>
    public struct EngineOptions
    {
        public PieceOwner startingPlayer;
        public bool evenOdds;

        public EngineOptions(PieceOwner startingPlayer, bool evenOdds)
        {
            this.startingPlayer = startingPlayer;
            this.evenOdds = evenOdds;
        }
    }
}
