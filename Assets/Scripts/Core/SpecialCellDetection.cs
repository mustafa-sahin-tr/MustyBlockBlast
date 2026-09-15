using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>One special cell that was destroyed and therefore owes an effect: where it stood and
    /// what kind it was. The position is kept because every special effect that will exist is
    /// positional (a blast radius, a row, a column), and once the cell is cleared the board no longer
    /// knows where the kind came from.</summary>
    public readonly struct SpecialCellTrigger
    {
        public SpecialCellTrigger(GridPosition position, SpecialCellKind kind)
        {
            Position = position;
            Kind = kind;
        }

        public GridPosition Position { get; }

        public SpecialCellKind Kind { get; }
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

                results.Add(new SpecialCellTrigger(position, kind));
            }
        }
    }
}
