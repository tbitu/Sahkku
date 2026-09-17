using System.Collections.Generic;

namespace Sahkku.Rules
{
    /// <summary>Movement pattern ids used by <see cref="PieceRules.moves"/>.</summary>
    public static class MovePatterns
    {
        /// <summary>Along the board track in the moving player's forward direction.</summary>
        public const string Forward = "forward";

        /// <summary>Opposite to <see cref="Forward"/>.</summary>
        public const string Backward = "backward";

        /// <summary>Straight up/down between rows (same x, y ± steps), for pieces that may cross rows.</summary>
        public const string Vertical = "vertical";
    }

    /// <summary>Track leg directions, expressed in board coordinates (the PDF wording: "right"/"left").</summary>
    public static class TrackDirections
    {
        public const string Right = "right";
        public const string Left = "left";
    }

    /// <summary>Starting-player rules.</summary>
    public static class StartModes
    {
        /// <summary>Players throw in turn; the first to roll a sáhkku (X) starts.</summary>
        public const string FirstSahhku = "firstSahhku";

        /// <summary>Each player throws the whole set once; the most sáhkku (X) faces start.</summary>
        public const string MostSahhku = "mostSahhku";
    }

    /// <summary>
    /// A complete, data-driven description of the sáhkku ruleset. Authored in JSON
    /// (Assets/Resources/SahkkuRules.json) and interpreted by <see cref="RulesEngine"/>. This type is
    /// shared by the game engine and by future LLM-driven NPCs.
    /// </summary>
    public class RuleSet
    {
        public string id;
        public int version;
        public BoardRules board;
        public TrackRules track;
        public PieceSetRules pieces;
        public DiceRules dice;
        public ActivationRules activation;
        public InactiveRules inactive;
        public StartRules start;
        public SetupRules setup;
        public VariantSet variants;
        public WinRules win;

        public PieceRules Def(PieceType type)
        {
            if (type == PieceType.Soldier) return pieces.soldier;
            if (type == PieceType.King) return pieces.king;
            return pieces.queen;
        }

        public DieFaceRules FaceRules(DieFace face)
        {
            for (int i = 0; i < dice.faces.Length; ++i)
            {
                if (dice.faces[i].id == FaceId(face)) return dice.faces[i];
            }
            throw new RuleSetException("Ruleset is missing the '" + FaceId(face) + "' die face.");
        }

        /// <summary>The ruleset face id of a <see cref="DieFace"/>. Identity only; the JSON decides steps and order.</summary>
        public static string FaceId(DieFace face)
        {
            switch (face)
            {
                case DieFace.Sahhku: return "sahhku";
                case DieFace.Three: return "three";
                case DieFace.Two: return "two";
                default: return "zero";
            }
        }

        public DieFace ResolveFace(string id)
        {
            for (int i = 0; i < dice.faces.Length; ++i)
            {
                if (dice.faces[i].id == id) return (DieFace)i;
            }
            throw new RuleSetException("Unknown die face id '" + id + "'.");
        }

        /// <summary>Sort key for a face: its position in <c>dice.useOrder</c>. Lower is spent first.</summary>
        public int UseOrderOf(DieFace face)
        {
            for (int i = 0; i < dice.useOrder.Length; ++i)
            {
                if (dice.useOrder[i] == FaceId(face)) return i;
            }
            throw new RuleSetException("Die face '" + FaceId(face) + "' is not listed in 'dice.useOrder'.");
        }

        /// <summary>Throws <see cref="RuleSetException"/> if the ruleset is incomplete or inconsistent.</summary>
        public void Validate()
        {
            if (board == null) throw new RuleSetException("Ruleset is missing 'board'.");
            if (board.width <= 0 || board.height <= 0) throw new RuleSetException("'board.width' and 'board.height' must be positive.");
            if (board.layout != "rowMajorSnake") throw new RuleSetException("Unsupported board layout '" + board.layout + "'; expected 'rowMajorSnake'.");

            ValidateTrack();
            ValidatePieces();
            ValidateDice();

            if (activation == null || activation.onMoveInactivePiece == null)
                throw new RuleSetException("Ruleset must define 'activation.onMoveInactivePiece'.");

            if (inactive == null) throw new RuleSetException("Ruleset is missing 'inactive'.");

            if (start == null) throw new RuleSetException("Ruleset is missing 'start'.");
            if (start.mode != StartModes.FirstSahhku && start.mode != StartModes.MostSahhku)
                throw new RuleSetException("Unsupported 'start.mode' '" + start.mode + "'; expected '" + StartModes.FirstSahhku + "' or '" + StartModes.MostSahhku + "'.");

            ValidateSetup();
            ValidateVariants();

            if (win == null) throw new RuleSetException("Ruleset is missing 'win'.");
            if (!win.opponentSoldiersExhausted && !HasLandingEndsGamePiece())
                throw new RuleSetException("Ruleset defines no way to win: enable 'win.opponentSoldiersExhausted' or 'landingEndsGame' on a piece.");
        }

        void ValidateTrack()
        {
            if (track == null || track.legs == null || track.legs.Length == 0)
                throw new RuleSetException("Ruleset must define at least one 'track.legs' entry.");

            bool[] covered = new bool[board.height];
            for (int i = 0; i < track.legs.Length; ++i)
            {
                TrackLegRules leg = track.legs[i];
                if (leg == null) throw new RuleSetException("'track.legs[" + i + "]' is null.");
                if (leg.row < 0 || leg.row >= board.height)
                    throw new RuleSetException("'track.legs[" + i + "].row' is off the board.");
                if (leg.direction != TrackDirections.Right && leg.direction != TrackDirections.Left)
                    throw new RuleSetException("'track.legs[" + i + "].direction' must be '" + TrackDirections.Right + "' or '" + TrackDirections.Left + "'.");
                covered[leg.row] = true;
            }

            for (int row = 0; row < board.height; ++row)
            {
                if (!covered[row]) throw new RuleSetException("'track.legs' never visits board row " + row + ".");
            }

            if (track.legs[0].row != 0 && track.legs[0].row != board.height - 1)
                throw new RuleSetException("'track.legs[0]' must start on a home row (row 0 or row " + (board.height - 1) + ").");

            // Legs must join end-to-end with a bend: consecutive legs share the join column but not
            // the row, and the lap must close.
            for (int i = 0; i < track.legs.Length; ++i)
            {
                TrackLegRules from = track.legs[i];
                TrackLegRules to = track.legs[(i + 1) % track.legs.Length];
                int endX = from.direction == TrackDirections.Right ? board.width - 1 : 0;
                int startX = to.direction == TrackDirections.Right ? 0 : board.width - 1;
                if (endX != startX)
                    throw new RuleSetException("'track.legs[" + i + "]' ends at x=" + endX + " but the next leg starts at x=" + startX + "; legs must join with a bend.");
                if (from.row == to.row)
                    throw new RuleSetException("'track.legs[" + i + "]' and the next leg both stay on row " + from.row + "; a leg must end by bending onto another row.");
            }
        }

        void ValidatePieces()
        {
            if (pieces == null || pieces.soldier == null || pieces.queen == null || pieces.king == null)
                throw new RuleSetException("Ruleset must define 'soldier', 'queen' and 'king' pieces.");
            ValidatePiece("soldier", pieces.soldier);
            ValidatePiece("queen", pieces.queen);
            ValidatePiece("king", pieces.king);
        }

        void ValidatePiece(string name, PieceRules piece)
        {
            if (piece.moves == null || piece.moves.Length == 0)
                throw new RuleSetException("Piece '" + name + "' must declare at least one movement pattern.");
            for (int i = 0; i < piece.moves.Length; ++i)
            {
                string move = piece.moves[i];
                if (move != MovePatterns.Forward && move != MovePatterns.Backward && move != MovePatterns.Vertical)
                    throw new RuleSetException("Piece '" + name + "' has unknown movement pattern '" + move + "'.");
            }

            if (piece.recruitedWhenLanded && piece.capturable)
                throw new RuleSetException("Piece '" + name + "' cannot be both 'capturable' and 'recruitedWhenLanded'.");
            if (piece.landingEndsGame && !piece.capturable)
                throw new RuleSetException("Piece '" + name + "' declares 'landingEndsGame' but is not 'capturable', so the ending can never happen.");
            if (piece.recruitsKingOnEnemyHomeRow && piece.recruitedWhenLanded)
                throw new RuleSetException("Piece '" + name + "' cannot recruit the king both by landing and by entering the enemy home row.");
        }

        void ValidateDice()
        {
            if (dice == null) throw new RuleSetException("Ruleset is missing 'dice'.");
            if (dice.count <= 0) throw new RuleSetException("'dice.count' must be positive.");
            if (dice.faces == null || dice.faces.Length != 4)
                throw new RuleSetException("A four-sided sáhkku die needs exactly 4 faces (sahhku, three, two, zero).");

            string[] expected = { "sahhku", "three", "two", "zero" };
            for (int i = 0; i < expected.Length; ++i)
            {
                DieFaceRules face = FindFace(expected[i]);
                if (face == null) throw new RuleSetException("Die face '" + expected[i] + "' must be declared exactly once in 'dice.faces'.");
                if (face.steps < 0) throw new RuleSetException("'dice.faces[" + expected[i] + "].steps' must not be negative.");
            }
            if (dice.faces.Length != expected.Length)
                throw new RuleSetException("'dice.faces' must declare the four sáhkku faces exactly once each.");

            if (dice.useOrder == null || dice.useOrder.Length != expected.Length)
                throw new RuleSetException("'dice.useOrder' must list the four sáhkku faces in the order they are spent.");
            for (int i = 0; i < expected.Length; ++i)
            {
                if (FindIn(dice.useOrder, expected[i]) < 0)
                    throw new RuleSetException("'dice.useOrder' must list '" + expected[i] + "' exactly once.");
            }

            if (string.IsNullOrEmpty(dice.activateFace)) throw new RuleSetException("'dice.activateFace' is required.");
            ResolveFace(dice.activateFace);

            if (dice.reroll == null) throw new RuleSetException("Ruleset is missing 'dice.reroll'.");
            if (dice.reroll.faces == null || dice.reroll.faces.Length == 0)
                throw new RuleSetException("'dice.reroll.faces' must list at least one re-rollable face.");
            for (int i = 0; i < dice.reroll.faces.Length; ++i)
            {
                ResolveFace(dice.reroll.faces[i]);
            }
        }

        void ValidateSetup()
        {
            if (setup == null) throw new RuleSetException("Ruleset is missing 'setup'.");
            ValidateRow("setup.soldiers.P1", setup.soldiers == null ? null : setup.soldiers.P1);
            ValidateRow("setup.soldiers.P2", setup.soldiers == null ? null : setup.soldiers.P2);
            ValidatePlacement("setup.queens.P1", setup.queens == null ? null : setup.queens.P1);
            ValidatePlacement("setup.queens.P2", setup.queens == null ? null : setup.queens.P2);
            ValidatePlacement("setup.king", setup.king);

            if (setup.soldiers.P1.row == setup.soldiers.P2.row)
                throw new RuleSetException("'setup.soldiers.P1.row' and 'setup.soldiers.P2.row' must differ.");

            var taken = new Dictionary<int, string>();
            MarkRow(taken, "setup.soldiers.P1", setup.soldiers.P1.row);
            MarkRow(taken, "setup.soldiers.P2", setup.soldiers.P2.row);
            MarkPlacement(taken, "setup.queens.P1", setup.queens.P1);
            MarkPlacement(taken, "setup.queens.P2", setup.queens.P2);
            MarkPlacement(taken, "setup.king", setup.king);
        }

        void ValidateVariants()
        {
            if (variants == null) throw new RuleSetException("Ruleset is missing 'variants'.");
            if (variants.standard == null) throw new RuleSetException("Ruleset must define a 'standard' variant.");
            if (variants.evenOdds == null) throw new RuleSetException("Ruleset must define an 'evenOdds' variant.");
            ValidateVariant("standard", variants.standard);
            ValidateVariant("evenOdds", variants.evenOdds);
        }

        void ValidateVariant(string name, VariantRules variant)
        {
            if (variant.soldiersActive < 0 || variant.soldiersActive >= board.width)
                throw new RuleSetException("'variants." + name + ".soldiersActive' must be between 0 and " + (board.width - 1) + " (at least one soldier must still be waiting to be activated).");
        }

        bool HasLandingEndsGamePiece()
        {
            return pieces.soldier.landingEndsGame || pieces.queen.landingEndsGame || pieces.king.landingEndsGame;
        }

        DieFaceRules FindFace(string id)
        {
            for (int i = 0; i < dice.faces.Length; ++i)
            {
                if (dice.faces[i] != null && dice.faces[i].id == id) return dice.faces[i];
            }
            return null;
        }

        static int FindIn(string[] values, string value)
        {
            for (int i = 0; i < values.Length; ++i)
            {
                if (values[i] == value) return i;
            }
            return -1;
        }

        void ValidateRow(string path, RowPlacement placement)
        {
            if (placement == null) throw new RuleSetException("'" + path + "' is required.");
            if (placement.placement != "all")
                throw new RuleSetException("'" + path + ".placement' must be 'all'.");
            if (placement.row < 0 || placement.row >= board.height)
                throw new RuleSetException("'" + path + ".row' is off the board.");
        }

        void ValidatePlacement(string path, Placement placement)
        {
            if (placement == null) throw new RuleSetException("'" + path + "' is required.");
            if (placement.row < 0 || placement.row >= board.height)
                throw new RuleSetException("'" + path + ".row' is off the board.");
            if (placement.x < 0 || placement.x >= board.width)
                throw new RuleSetException("'" + path + ".x' is off the board.");
        }

        void MarkPlacement(Dictionary<int, string> taken, string path, Placement placement)
        {
            Mark(taken, path, placement.x, placement.row);
        }

        void MarkRow(Dictionary<int, string> taken, string path, int row)
        {
            for (int x = 0; x < board.width; ++x) Mark(taken, path, x, row);
        }

        void Mark(Dictionary<int, string> taken, string path, int x, int row)
        {
            int place = BoardLayout.IndexFromCoordinates(board.width, board.height, x, row);
            string other;
            if (taken.TryGetValue(place, out other))
                throw new RuleSetException("'" + path + "' overlaps '" + other + "' at x=" + x + ", y=" + row + ".");
            taken[place] = path;
        }
    }

    public class BoardRules
    {
        public int width;
        public int height;
        public string layout;
    }

    /// <summary>
    /// The "8"-shaped track that soldiers, kings and queens walk. The legs describe the board
    /// *cells* in the order a piece visits them during one lap, for player 1; the same lap
    /// expressed in board coordinates is the PDF's "move to the right on your home row, to the
    /// left on the middle row, to the right on the enemy row, ...".
    ///
    /// The lap length is <c>legs.Length * board.width</c>, which is why a piece's position cannot be
    /// recovered from its board cell alone: the middle row is visited twice per lap.
    /// </summary>
    public class TrackRules
    {
        public TrackLegRules[] legs;
    }

    public class TrackLegRules
    {
        /// <summary>Board row this leg runs along.</summary>
        public int row;

        /// <summary><see cref="TrackDirections.Right"/> or <see cref="TrackDirections.Left"/>.</summary>
        public string direction;
    }

    public class PieceSetRules
    {
        public PieceRules soldier;
        public PieceRules king;
        public PieceRules queen;
    }

    /// <summary>Per-piece rule primitives selected by the engine.</summary>
    public class PieceRules
    {
        /// <summary>Movement patterns from <see cref="MovePatterns"/>.</summary>
        public string[] moves;

        /// <summary>When true the step count comes from the current die; otherwise it is a single step.</summary>
        public bool movesScaleWithDie;

        /// <summary>Piece starts the game able to be activated (used for queens).</summary>
        public bool startActivatable;

        /// <summary>Activating this piece unlocks the next piece in the activation queue.</summary>
        public bool queuesNextOnActivation;

        /// <summary>Other pieces may not land on this piece when it is owned by the mover.</summary>
        public bool blocksOwnLanding;

        /// <summary>This piece may not land on a piece owned by the mover.</summary>
        public bool cannotLandOnOwnUnits;

        /// <summary>Landing on an opponent's piece of this type removes it and scores a capture.</summary>
        public bool capturable;

        /// <summary>Landing on a piece of this type recruits it to the mover instead of removing it.</summary>
        public bool recruitedWhenLanded;

        /// <summary>Landing on a piece of this type immediately ends the game in the mover's favour.</summary>
        public bool landingEndsGame;

        /// <summary>This piece recruits the neutral king by entering the opponent's home row.</summary>
        public bool recruitsKingOnEnemyHomeRow;

        public bool Has(string pattern)
        {
            if (moves == null) return false;
            for (int i = 0; i < moves.Length; ++i)
            {
                if (moves[i] == pattern) return true;
            }
            return false;
        }
    }

    public class DiceRules
    {
        public int count;

        /// <summary>Face that can activate a piece.</summary>
        public string activateFace;

        /// <summary>
        /// The order in which thrown dice must be spent (the PDF: "first use the X's, then the
        /// III's, then the II's"). Index 0 is spent first.
        /// </summary>
        public string[] useOrder;

        public RerollRules reroll;
        public DieFaceRules[] faces;
    }

    public class RerollRules
    {
        /// <summary>Faces that may be thrown again (the sáhkku X in this ruleset).</summary>
        public string[] faces;

        /// <summary>Re-rolls are only allowed before any die of this throw has been spent.</summary>
        public bool beforeUsingAnyDie;
    }

    public class DieFaceRules
    {
        public string id;
        public int steps;
    }

    public class ActivationRules
    {
        public SoldierActivationRule onMoveInactivePiece;
    }

    public class SoldierActivationRule
    {
        /// <summary>
        /// How many home-row lines (negative = towards the rear) from the mover's *source* line the
        /// soldier that becomes activatable stands. Soldiers must be activated in queue order, so
        /// the piece one line further back is the next one in the queue.
        /// </summary>
        public int unlockOffset;
    }

    /// <summary>Rules about pieces that have not been activated yet.</summary>
    public class InactiveRules
    {
        /// <summary>False when no piece may be moved onto a line holding an unactivated piece.</summary>
        public bool enterable;
    }

    public class StartRules
    {
        /// <summary>One of <see cref="StartModes"/>.</summary>
        public string mode;
    }

    public class SetupRules
    {
        public PerOwnerRow soldiers;
        public PerOwnerPlacement queens;
        public KingsPlacement king;
    }

    public class PerOwnerRow
    {
        public RowPlacement P1;
        public RowPlacement P2;
    }

    public class PerOwnerPlacement
    {
        public Placement P1;
        public Placement P2;
    }

    public class RowPlacement
    {
        public int row;
        public string placement;
    }

    public class Placement
    {
        public int row;
        public int x;
    }

    public class KingsPlacement : Placement
    {
        public string owner;
    }

    public class VariantSet
    {
        public VariantRules standard;
        public VariantRules evenOdds;
    }

    /// <summary>
    /// A starting position. The activation queue always runs from the foremost soldier of the home
    /// row towards the rear, so a variant only has to say how many soldiers are already loose.
    /// </summary>
    public class VariantRules
    {
        /// <summary>How many of the leading soldiers start activated; the next one starts activatable.</summary>
        public int soldiersActive;
    }

    public class WinRules
    {
        /// <summary>You win when the opponent has no soldiers left on the board.</summary>
        public bool opponentSoldiersExhausted;
    }
}
