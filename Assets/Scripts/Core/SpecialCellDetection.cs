using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Along which line (if any) a cell was destroyed. An effect that cares about direction — today
    /// <see cref="SpecialCellKind.Laser"/>, which fires at right angles to whatever took it out —
    /// reads this rather than re-deriving it, because once the cell is cleared the board no longer
    /// knows what removed it.
    /// <para>
    /// <see cref="None"/> is not "unknown": it is the honest answer for every destruction that has no
    /// line to it at all — a Bomb's 3x3, a Colour Cleanser's colour sweep — and an effect must treat it
    /// as such rather than guessing an axis.
    /// </para>
    /// </summary>
    public enum ClearAxis
    {
        /// <summary>Destroyed by something with no row or column to it.</summary>
        None = 0,

        /// <summary>Destroyed as part of a row being cleared.</summary>
        Row = 1,

        /// <summary>Destroyed as part of a column being cleared.</summary>
        Column = 2,

        /// <summary>Sat at the intersection of a cleared row and a cleared column, both in the same
        /// destruction — so it was destroyed by each of them at once.</summary>
        Both = 3,
    }

    /// <summary>One special cell that was destroyed and therefore owes an effect: where it stood, what
    /// kind it was, and along which line it went. The position is kept because every special effect
    /// that will exist is positional (a blast radius, a row, a column), and once the cell is cleared
    /// the board no longer knows where the kind came from.</summary>
    public readonly struct SpecialCellTrigger
    {
        /// <summary>A destruction with no axis to it — a Bomb, a Colour Cleanser. Equivalent to
        /// passing <see cref="ClearAxis.None"/>, and kept as its own overload so the callers that
        /// genuinely have no axis say so by omission rather than by naming one.</summary>
        public SpecialCellTrigger(GridPosition position, SpecialCellKind kind)
            : this(position, kind, ClearAxis.None)
        {
        }

        public SpecialCellTrigger(GridPosition position, SpecialCellKind kind, ClearAxis axis)
            : this(position, kind, axis, 0)
        {
        }

        public SpecialCellTrigger(GridPosition position, SpecialCellKind kind, ClearAxis axis, int coinValue)
            : this(position, kind, axis, coinValue, 0)
        {
        }

        public SpecialCellTrigger(
            GridPosition position, SpecialCellKind kind, ClearAxis axis, int coinValue, int diamondColourId)
        {
            Position = position;
            Kind = kind;
            Axis = axis;
            CoinValue = coinValue;
            DiamondColourId = diamondColourId;
        }

        public GridPosition Position { get; }

        public SpecialCellKind Kind { get; }

        /// <summary>The line this cell was destroyed along, or <see cref="ClearAxis.None"/> when
        /// whatever destroyed it had none.</summary>
        public ClearAxis Axis { get; }

        /// <summary>
        /// The coins this cell was priced at (<see cref="Board.GetCoinValue"/>), read at the one moment
        /// it was still known — for the same reason <see cref="Axis"/> and <see cref="Position"/> are
        /// carried: by the time <see cref="CoinEffect"/> runs the cell is cleared and the board has
        /// forgotten it. 0 for a coin that was never priced (a level-authored one), which pays the
        /// configured default, and 0 — meaningless and unread — for every kind that is not a coin.
        /// </summary>
        public int CoinValue { get; }

        /// <summary>
        /// The colour of this cell's <see cref="SpecialCellKind.Diamond"/> gem
        /// (<see cref="Board.GetDiamondColourId"/>), read at the one moment it was still known — for
        /// exactly the reason <see cref="CoinValue"/> is carried: by the time <see cref="DiamondClearEffect"/>
        /// runs the cell is cleared and the board has forgotten it. 0 — meaningless and unread — for
        /// every kind that is not a diamond.
        /// </summary>
        public int DiamondColourId { get; }
    }

    /// <summary>
    /// The single definition of "which of these cells being destroyed sets a special effect off".
    /// Shared by every path that destroys cells — the placement-driven
    /// <see cref="LineClearResolver"/>/<see cref="CascadeClearResolver"/> loop and the power-up-driven
    /// <see cref="PowerUpClearResolver"/> — so a special block behaves the same whether a completed
    /// line or a Bomb took it out, instead of each resolver carrying its own rule.
    /// </summary>
    public static class SpecialCellDetection
    {
        /// <summary>
        /// Appends a <see cref="SpecialCellTrigger"/> for each of <paramref name="destroyedCells"/>
        /// carrying a kind other than <see cref="SpecialCellKind.None"/>. <paramref name="results"/>
        /// is appended to, never cleared, so a caller can accumulate across several destroyed regions.
        /// <para>
        /// <b>Must be called before the cells are cleared.</b> <see cref="Board.Clear"/> resets a
        /// cell's kind along with its colour, so afterwards a destroyed special cell is
        /// indistinguishable from an ordinary one — the same "read it before you destroy it" ordering
        /// <see cref="PowerUpClearResolver"/> already applies to occupancy.
        /// </para>
        /// <para>
        /// Takes the destroyed cells as a plain list rather than a line index or a region shape, so it
        /// makes no assumption about board geometry: whatever produced the list decided the shape.
        /// </para>
        /// </summary>
        public static void CollectTriggered(
            Board board, IReadOnlyList<GridPosition> destroyedCells, List<SpecialCellTrigger> results)
            => CollectTriggered(board, destroyedCells, results, null, null);

        /// <summary>
        /// As <see cref="CollectTriggered(Board, IReadOnlyList{GridPosition}, List{SpecialCellTrigger})"/>,
        /// additionally recording which line each destroyed cell went with.
        /// <para>
        /// <paramref name="clearedRows"/> and <paramref name="clearedColumns"/> are the line indices the
        /// caller is in the middle of clearing; a destroyed cell whose row is among the former was
        /// destroyed by a row clear, whose column is among the latter by a column clear, and one that
        /// matches both sat at their intersection. A caller whose destruction has no lines to it (a
        /// Bomb, a Colour Cleanser) passes null for both, and every trigger comes back
        /// <see cref="ClearAxis.None"/>.
        /// </para>
        /// </summary>
        public static void CollectTriggered(
            Board board,
            IReadOnlyList<GridPosition> destroyedCells,
            List<SpecialCellTrigger> results,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (destroyedCells == null)
            {
                throw new ArgumentNullException(nameof(destroyedCells));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            for (int i = 0; i < destroyedCells.Count; i++)
            {
                GridPosition position = destroyedCells[i];
                SpecialCellKind kind = board.GetSpecialKind(position);
                if (kind == SpecialCellKind.None)
                {
                    continue;
                }

                results.Add(new SpecialCellTrigger(
                    position, kind, ResolveAxis(position, clearedRows, clearedColumns),
                    board.GetCoinValue(position), board.GetDiamondColourId(position)));
            }
        }

        private static ClearAxis ResolveAxis(
            GridPosition position, IReadOnlyList<int> clearedRows, IReadOnlyList<int> clearedColumns)
        {
            bool byRow = Contains(clearedRows, position.Y);
            bool byColumn = Contains(clearedColumns, position.X);

            if (byRow && byColumn)
            {
                return ClearAxis.Both;
            }

            if (byRow)
            {
                return ClearAxis.Row;
            }

            return byColumn ? ClearAxis.Column : ClearAxis.None;
        }

        /// <summary>Linear scan rather than a set: a clear covers at most a handful of lines, this runs
        /// once per destroyed cell in a once-per-placement path, and it allocates nothing.</summary>
        private static bool Contains(IReadOnlyList<int> indices, int value)
        {
            if (indices == null)
            {
                return false;
            }

            for (int i = 0; i < indices.Count; i++)
            {
                if (indices[i] == value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
