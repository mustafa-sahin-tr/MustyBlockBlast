namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The one rule every special-cell spawn selector shares (issue #441): a spawned kind never lands on,
    /// or immediately over, a cell that is already one special thing. Excluded are cells that carry any
    /// <see cref="SpecialCellKind"/> (a lock is <see cref="SpecialCellKind.Locked"/>, so this covers it),
    /// reinforced cells still standing (<see cref="Board.GetHitCount"/> above 0, whose kind is
    /// <see cref="SpecialCellKind.None"/>), and cells where a reinforced block or a lock broke during the
    /// resolution in flight (<see cref="Board.IsBrokenObstacle"/>) — empty and kind-less by then, but a
    /// reward there reads as stacked on the obstacle the player just watched go.
    /// <para>
    /// Two questions, because selectors spawn two ways: converting a block already standing in place
    /// (<see cref="CanConvert"/>), or occupying a cell a clear just emptied (<see cref="CanOccupy"/>).
    /// </para>
    /// </summary>
    public static class SpecialCellSpawnEligibility
    {
        /// <summary>True when the block standing at <paramref name="position"/> may be turned into a
        /// special cell in place: a real, occupied cell that is a plain block and nothing more.</summary>
        public static bool CanConvert(Board board, GridPosition position)
            => board.IsPlayable(position)
                && board.IsOccupied(position)
                && IsPlain(board, position);

        /// <summary>True when the empty cell at <paramref name="position"/> may be occupied by a spawned
        /// special cell: a real cell, empty, and not where an obstacle just broke.</summary>
        public static bool CanOccupy(Board board, GridPosition position)
            => board.IsPlayable(position)
                && !board.IsOccupied(position)
                && IsPlain(board, position);

        private static bool IsPlain(Board board, GridPosition position)
            => board.GetSpecialKind(position) == SpecialCellKind.None
                && board.GetHitCount(position) == 0
                && !board.IsBrokenObstacle(position);
    }
}
