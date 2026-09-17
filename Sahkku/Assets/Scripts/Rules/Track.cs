using System.Collections.Generic;

namespace Sahkku.Rules
{
    /// <summary>The board's cell numbering, kept in one place so the engine and the track agree.</summary>
    public static class BoardLayout
    {
        /// <summary>Maps board coordinates onto the S-shaped path index; returns -1 when off the board.</summary>
        public static int IndexFromCoordinates(int width, int height, int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return -1;
            int i = (y % 2 == 1) ? (width - 1 - x) : x;
            return y * width + i;
        }

        public static void CoordinatesFromIndex(int width, int index, out int x, out int y)
        {
            y = index / width;
            int i = index % width;
            x = (y % 2 == 1) ? (width - 1 - i) : i;
        }
    }

    /// <summary>
    /// Arc ↔ board-cell mapping for one player, built from the ruleset's <see cref="TrackRules"/>.
    ///
    /// A lap visits <c>legs.Length * width</c> cells. The middle row is visited twice per lap, so
    /// two arcs map onto the same <see cref="Place"/>; a piece must therefore store its arc, and
    /// <see cref="PlaceOf"/> is the only single-valued direction.
    /// </summary>
    public sealed class BoardTrack
    {
        readonly int width;
        readonly int height;
        readonly int[] placeOfArc;
        readonly int[] legRows;

        BoardTrack(int width, int height, int[] placeOfArc, int[] legRows)
        {
            this.width = width;
            this.height = height;
            this.placeOfArc = placeOfArc;
            this.legRows = legRows;
        }

        /// <summary>Number of arcs in one lap.</summary>
        public int Length { get { return placeOfArc.Length; } }

        public int LegCount { get { return legRows.Length; } }

        public int RowOfLeg(int leg) { return legRows[leg]; }

        /// <summary>The row a piece starts on and returns to at the end of a lap.</summary>
        public int HomeRow { get { return legRows[0]; } }

        public static BoardTrack Build(BoardRules board, TrackRules track)
        {
            int legCount = track.legs.Length;
            var placeOfArc = new int[legCount * board.width];
            var legRows = new int[legCount];

            for (int leg = 0; leg < legCount; ++leg)
            {
                TrackLegRules rules = track.legs[leg];
                legRows[leg] = rules.row;
                for (int offset = 0; offset < board.width; ++offset)
                {
                    int x = rules.direction == TrackDirections.Right ? offset : board.width - 1 - offset;
                    placeOfArc[leg * board.width + offset] =
                        BoardLayout.IndexFromCoordinates(board.width, board.height, x, rules.row);
                }
            }

            return new BoardTrack(board.width, board.height, placeOfArc, legRows);
        }

        /// <summary>The board-mirrored track, i.e. the track of the player at the opposite end.</summary>
        public BoardTrack Mirror()
        {
            var mirrored = new int[placeOfArc.Length];
            for (int arc = 0; arc < placeOfArc.Length; ++arc)
            {
                int x, y;
                BoardLayout.CoordinatesFromIndex(width, placeOfArc[arc], out x, out y);
                mirrored[arc] = BoardLayout.IndexFromCoordinates(width, height, width - 1 - x, height - 1 - y);
            }
            return new BoardTrack(width, height, mirrored, MirrorRows());
        }

        int[] MirrorRows()
        {
            var rows = new int[legRows.Length];
            for (int i = 0; i < legRows.Length; ++i) rows[i] = height - 1 - legRows[i];
            return rows;
        }

        public int LegOf(int arc) { return Normalize(arc) / width; }

        public int OffsetOf(int arc) { return Normalize(arc) % width; }

        /// <summary>The board cell an arc points at.</summary>
        public int PlaceOf(int arc) { return placeOfArc[Normalize(arc)]; }

        /// <summary>The first arc that points at a board cell, or -1 when the cell is not on this track.</summary>
        public int FirstArcOf(int place)
        {
            for (int arc = 0; arc < placeOfArc.Length; ++arc)
            {
                if (placeOfArc[arc] == place) return arc;
            }
            return -1;
        }

        /// <summary>The arc of a cell inside one leg, or -1 when that leg does not cover the cell.</summary>
        public int ArcOfPlaceInLeg(int leg, int place)
        {
            int start = leg * width;
            for (int offset = 0; offset < width; ++offset)
            {
                if (placeOfArc[start + offset] == place) return start + offset;
            }
            return -1;
        }

        /// <summary>
        /// Resolves where a piece crossing between rows lands on the track. A crossing keeps the
        /// piece's column and enters the target row at that column; when the target row is visited
        /// by two legs (the middle row) the piece continues into the *next* leg, which is what makes
        /// a crossing count as "straight on" rather than a turn.
        /// </summary>
        public int CrossingArc(int fromArc, int targetRow, int targetPlace)
        {
            int fromLeg = LegOf(fromArc);
            int target = -1;
            int covered = 0;
            for (int leg = 0; leg < legRows.Length; ++leg)
            {
                if (legRows[leg] != targetRow) continue;
                covered++;
                if (target < 0) target = leg;
                if (leg == fromLeg + 1) target = leg;
            }
            if (covered == 0) return -1;
            if (covered > 1 && target == -1) return -1;
            return ArcOfPlaceInLeg(target, targetPlace);
        }

        /// <summary>The line <paramref name="offset"/> lines further along the track, clamped to the lap.</summary>
        public int ArcInHomeRow(int offset)
        {
            if (offset < 0 || offset >= width) return -1;
            return offset;
        }

        /// <summary>Bring an arc into <c>[0, Length)</c>, so a piece's stored arc stays canonical.</summary>
        public int Normalize(int arc)
        {
            int length = placeOfArc.Length;
            if (length == 0) throw new RuleSetException("The track has no legs.");
            int wrapped = arc % length;
            return wrapped < 0 ? wrapped + length : wrapped;
        }
    }
}
