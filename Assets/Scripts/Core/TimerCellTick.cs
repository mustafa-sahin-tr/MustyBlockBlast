using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The global per-placement tick every <see cref="SpecialCellKind.Timer"/> cell on the board takes,
    /// mirroring <see cref="ReinforcedCellDamage"/>'s role as the one place a per-cell numeric state is
    /// read, mutated and cleared — except this one is not gated on the cell being touched at all
    /// (issue #307 AC2/AC9): it decrements <em>every</em> timer cell by exactly one, once per successful
    /// placement, regardless of whether that placement was anywhere near it.
    /// <para>
    /// A board-wide scan rather than an index list the caller maintains, deliberately: nothing else on
    /// this project tracks "which cells are currently timer cells" as a live set (a timer cell's kind can
    /// change from underneath — cleared by a line, moved by a Vortex pull, expired by an earlier tick),
    /// so re-deriving the set from <see cref="Board.GetSpecialKind"/> each call is the only way to stay
    /// correct without a second piece of state to keep in sync. It runs once per placement, over the
    /// board's cells (64 on the standard board), never per frame — the same cost class
    /// <see cref="Board.OccupiedCellCount"/> and <c>BoardModel.NotifyHitCountsRefreshed</c> already
    /// accept for a once-per-placement pass.
    /// </para>
    /// </summary>
    public static class TimerCellTick
    {
        /// <summary>
        /// Decrements every timer cell's countdown by one. A cell whose countdown reaches zero converts
        /// to an ordinary cell in place — <see cref="Board.SetSpecialKind"/> to
        /// <see cref="SpecialCellKind.None"/>, countdown zeroed, occupied block and colour left alone
        /// (issue #307 AC4) — and its position is appended to <paramref name="expiredCells"/>, which is
        /// cleared first.
        /// </summary>
        public static void Tick(Board board, List<GridPosition> expiredCells)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (expiredCells == null)
            {
                throw new ArgumentNullException(nameof(expiredCells));
            }

            expiredCells.Clear();

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    if (board.GetSpecialKind(position) != SpecialCellKind.Timer)
                    {
                        continue;
                    }

                    int remaining = board.GetTimerCountdown(position) - 1;
                    if (remaining <= 0)
                    {
                        board.SetSpecialKind(position, SpecialCellKind.None);
                        board.SetTimerCountdown(position, 0);
                        expiredCells.Add(position);
                        continue;
                    }

                    board.SetTimerCountdown(position, remaining);
                }
            }
        }
    }
}
