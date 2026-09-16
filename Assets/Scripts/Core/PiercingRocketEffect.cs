using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// <see cref="SpecialPieceKind.PiercingRocket"/>'s effect: the full row and the full column running
    /// through the cell it was placed on are emptied, unconditionally.
    /// <para>
    /// Deliberately <em>not</em> an <see cref="ISpecialCellEffect"/> and deliberately not
    /// <see cref="LaserEffect"/>. A laser is a cell that fires when something destroys it, so it has an
    /// axis to compute an opposite from and a chain to walk; a rocket is a <em>piece</em> that fires
    /// when the player places it, so there is no "whatever destroyed it" to read, no axis to choose
    /// between and no chaining — it always wipes both lines, exactly once. Reusing the laser's chaining
    /// loop would import a decision this effect does not have to make.
    /// </para>
    /// <para>
    /// Fullness is irrelevant, exactly as it is for a laser's wipe or a Row Clear power-up: a wipe
    /// empties every occupied cell of the line whether or not the line was complete. It must therefore
    /// run <em>before</em> the placement's normal full-line resolution, so a cell this wipe removed can
    /// never also be cleared — and scored — as part of a completed line.
    /// </para>
    /// <para>
    /// The rocket's own cell is on both lines and is emptied with them: that is the self-destruct, and
    /// it needs no special case.
    /// </para>
    /// <para>
    /// Long-lived by design, exactly as <see cref="LaserEffect"/> is: one instance per System that
    /// resolves placements, with <see cref="BeginResolution"/> called before each one rather than a
    /// fresh instance allocated. <see cref="WipedCells"/> and <see cref="TriggeredSpecials"/> are
    /// therefore buffers this instance owns and overwrites — a caller that needs either beyond the
    /// current resolution must copy it.
    /// </para>
    /// </summary>
    public sealed class PiercingRocketEffect
    {
        private readonly List<GridPosition> _wipedCells = new List<GridPosition>(Board.SIZE * 2);
        private readonly List<SpecialCellTrigger> _triggeredSpecials =
            new List<SpecialCellTrigger>(Board.SIZE * 2);

        /// <summary>Every cell of the two lines, occupied or not, before the occupied ones are picked
        /// out of it. Reused rather than allocated per call, exactly as the result buffers are.</summary>
        private readonly List<GridPosition> _lineBuffer = new List<GridPosition>(Board.SIZE * 2);

        /// <summary>Every cell this effect emptied since the last <see cref="BeginResolution"/>. Only
        /// cells that actually held a block are listed: an already-empty cell on a wiped line was not
        /// destroyed and must not be scored or repainted as if it were.</summary>
        public IReadOnlyList<GridPosition> WipedCells => _wipedCells;

        /// <summary>
        /// The special cells the wipe destroyed, collected through the same
        /// <see cref="SpecialCellDetection"/> pass every other destroying path uses — a core taken out
        /// by a rocket detonates exactly as one taken out by a completed line does.
        /// <para>
        /// Reported rather than applied here: this class owns the wipe, and whether the triggers it
        /// produced go on to fire is the calling System's call, made with real effects in hand.
        /// </para>
        /// </summary>
        public IReadOnlyList<SpecialCellTrigger> TriggeredSpecials => _triggeredSpecials;

        /// <summary>Starts a new resolution: forgets the previous one's wiped cells and triggers. Must
        /// be called before the placement that will apply this effect, or two placements' cells would be
        /// reported as one.</summary>
        public void BeginResolution()
        {
            _wipedCells.Clear();
            _triggeredSpecials.Clear();
        }

        /// <summary>
        /// Empties every occupied cell of <paramref name="origin"/>'s row and column. The intersection —
        /// <paramref name="origin"/> itself — is listed once: the column pass skips the row the row pass
        /// already covered, matching the "count intersections once" rule every other clear follows.
        /// </summary>
        public void Apply(Board board, GridPosition origin)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (!Board.IsInside(origin))
            {
                throw new ArgumentOutOfRangeException(nameof(origin), origin, "Outside the board.");
            }

            _lineBuffer.Clear();
            board.CollectRowCells(origin.Y, _lineBuffer);
            board.CollectColumnCells(origin.X, _lineBuffer);

            // The occupied cells are picked out — and the special kinds among them read — before a
            // single cell is cleared: Board.Clear resets a cell's kind along with its colour, so this is
            // the last moment either is knowable.
            int firstNewCell = _wipedCells.Count;
            for (int i = 0; i < _lineBuffer.Count; i++)
            {
                GridPosition cell = _lineBuffer[i];

                // The column pass re-walks the intersection the row pass already listed. Skipping it by
                // row index rather than by searching the results keeps this O(line length).
                if (i >= Board.SIZE && cell.Y == origin.Y)
                {
                    continue;
                }

                if (board.IsOccupied(cell))
                {
                    _wipedCells.Add(cell);
                }
            }

            for (int i = firstNewCell; i < _wipedCells.Count; i++)
            {
                GridPosition cell = _wipedCells[i];
                SpecialCellKind kind = board.GetSpecialKind(cell);
                if (kind != SpecialCellKind.None)
                {
                    _triggeredSpecials.Add(new SpecialCellTrigger(
                        cell, kind, ResolveAxis(cell, origin)));
                }
            }

            for (int i = firstNewCell; i < _wipedCells.Count; i++)
            {
                board.Clear(_wipedCells[i]);
            }
        }

        /// <summary>Which of the two wiped lines took <paramref name="cell"/> out. The intersection went
        /// with both, exactly as a cell caught by a simultaneous row and column clear does.</summary>
        private static ClearAxis ResolveAxis(GridPosition cell, GridPosition origin)
        {
            bool byRow = cell.Y == origin.Y;
            bool byColumn = cell.X == origin.X;

            if (byRow && byColumn)
            {
                return ClearAxis.Both;
            }

            return byRow ? ClearAxis.Row : ClearAxis.Column;
        }
    }
}
