using System.Collections.Generic;

namespace Sahkku.Rules
{
    /// <summary>Movement pattern ids used by <see cref="PieceRules.moves"/>.</summary>
    public static class MovePatterns
    {
        /// <summary>Along the board path in the moving player's forward direction.</summary>
        public const string Forward = "forward";

        /// <summary>Opposite to <see cref="Forward"/>.</summary>
        public const string Backward = "backward";

        /// <summary>Straight up/down between rows (same x, y ± steps).</summary>
        public const string Vertical = "vertical";
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
        public PieceSetRules pieces;
        public DiceRules dice;
        public ActivationRules activation;
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
            return dice.faces[(int)face];
        }

        public DieFace ResolveFace(string id)
        {
            for (int i = 0; i < dice.faces.Length; ++i)
            {
                if (dice.faces[i].id == id) return (DieFace)i;
            }
            throw new RuleSetException("Unknown die face id '" + id + "'.");
        }

        /// <summary>Throws <see cref="RuleSetException"/> if the ruleset is incomplete or inconsistent.</summary>
        public void Validate()
        {
            if (board == null) throw new RuleSetException("Ruleset is missing 'board'.");
            if (board.width <= 0 || board.height <= 0) throw new RuleSetException("'board.width' and 'board.height' must be positive.");
            if (board.layout != "rowMajorSnake") throw new RuleSetException("Unsupported board layout '" + board.layout + "'; expected 'rowMajorSnake'.");

            if (pieces == null || pieces.soldier == null || pieces.queen == null || pieces.king == null)
                throw new RuleSetException("Ruleset must define 'soldier', 'queen' and 'king' pieces.");
            ValidatePiece("soldier", pieces.soldier);
            ValidatePiece("queen", pieces.queen);
            ValidatePiece("king", pieces.king);

            if (dice == null) throw new RuleSetException("Ruleset is missing 'dice'.");
            if (dice.count <= 0) throw new RuleSetException("'dice.count' must be positive.");
            if (dice.faces == null || dice.faces.Length != 4)
                throw new RuleSetException("A four-sided sáhkku die needs exactly 4 faces (sahhku, three, two, zero).");
            string[] expectedFaces = { "sahhku", "three", "two", "zero" };
            for (int i = 0; i < expectedFaces.Length; ++i)
            {
                if (dice.faces[i] == null || dice.faces[i].id != expectedFaces[i])
                    throw new RuleSetException("Die face " + i + " must be '" + expectedFaces[i] + "'.");
            }
            if (dice.order != "ascendingFaceId") throw new RuleSetException("Unsupported 'dice.order' '" + dice.order + "'; expected 'ascendingFaceId'.");
            if (string.IsNullOrEmpty(dice.activateFace)) throw new RuleSetException("'dice.activateFace' is required.");
            ResolveFace(dice.activateFace);
            if (!string.IsNullOrEmpty(dice.rerollFace)) ResolveFace(dice.rerollFace);

            if (activation == null || activation.onMoveInactiveSoldier == null)
                throw new RuleSetException("Ruleset must define 'activation.onMoveInactiveSoldier'.");
            if (activation.onMoveInactiveSoldier.relativeTo != "source")
                throw new RuleSetException("'activation.onMoveInactiveSoldier.relativeTo' must be 'source'.");

            if (setup == null) throw new RuleSetException("Ruleset is missing 'setup'.");
            ValidateRow("setup.soldiers.P1", setup.soldiers == null ? null : setup.soldiers.P1);
            ValidateRow("setup.soldiers.P2", setup.soldiers == null ? null : setup.soldiers.P2);
            ValidatePlacement("setup.queens.P1", setup.queens == null ? null : setup.queens.P1);
            ValidatePlacement("setup.queens.P2", setup.queens == null ? null : setup.queens.P2);
            ValidatePlacement("setup.king", setup.king);

            if (variants == null) throw new RuleSetException("Ruleset is missing 'variants'.");
            if (variants.standard == null) throw new RuleSetException("Ruleset must define a 'standard' variant.");
            if (variants.evenOdds == null) throw new RuleSetException("Ruleset must define an 'evenOdds' variant.");

            if (win == null) throw new RuleSetException("Ruleset is missing 'win'.");
            if (win.soldierCapturesToWin <= 0) throw new RuleSetException("'win.soldierCapturesToWin' must be positive.");
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
    }

    public class BoardRules
    {
        public int width;
        public int height;
        public string layout;
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

        /// <summary>Recruited by a soldier reaching the enemy's home territory.</summary>
        public bool recruitedOnEnemyTerritory;

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

        /// <summary>How dice are ordered after a throw; only 'ascendingFaceId' is supported.</summary>
        public string order;

        /// <summary>Face that can activate a piece.</summary>
        public string activateFace;

        /// <summary>Face that grants a re-roll.</summary>
        public string rerollFace;

        /// <summary>Re-rolls are only allowed while the first die is the active one.</summary>
        public bool rerollRequiresFirstDie;

        public DieFaceRules[] faces;
    }

    public class DieFaceRules
    {
        public string id;
        public int steps;
    }

    public class ActivationRules
    {
        public SoldierActivationRule onMoveInactiveSoldier;
    }

    public class SoldierActivationRule
    {
        /// <summary>Offset origin; only 'source' (the mover's original place) is supported.</summary>
        public string relativeTo;

        /// <summary>Offset applied to the source place index.</summary>
        public int offset;

        /// <summary>Negate the offset for player 2 (mirrors the two directions of travel).</summary>
        public bool perPlayerSign;
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

    public class VariantRules
    {
        public CoordRules[] active;
        public CoordRules[] activatable;
    }

    public class CoordRules
    {
        public int x;
        public int y;
    }

    public class WinRules
    {
        public int soldierCapturesToWin;
    }
}
