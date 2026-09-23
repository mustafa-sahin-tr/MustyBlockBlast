namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Where a diamond may be written onto the board when a decorated piece lands (issue #394 AC3).
    /// <para>
    /// A diamond never overlaps a reinforced or timer cell — those are level-authored mechanics with
    /// their own reading of what a cell is, and a gem layered on top would either be uncollectable (a
    /// reinforced cell that only spends a hit) or lose its kind on expiry (a timer cell converting to
    /// ordinary). In ordinary play this can never arise: a piece is only ever placed onto empty cells,
    /// and both mechanics occupy theirs. The rule is stated here all the same, so the placement loop
    /// asks a named question rather than relying on legality elsewhere, and so any future path that
    /// writes a piece onto a pre-filled cell inherits the same answer.
    /// </para>
    /// <para>
    /// Nor does a diamond ever land on an ice socket (issue #433, product decision). Unlike the two
    /// above this one CAN arise in ordinary play — an ice socket is an empty, playable cell by design —
    /// so it is a real gate, not a stated invariant: a decorated cell that lands on a position with
    /// ice still to melt is placed as an ordinary block of the piece's colour instead, and the diamond
    /// is simply not written. Two readings of the same cell ("clear me to melt the ice" and "clear me
    /// to collect the gem") would otherwise compete for one destruction, and the ice overlay would sit
    /// on top of the gem icon.
    /// </para>
    /// <para>
    /// <b>Level-authoring guideline, not enforced in code:</b> a level should not combine an
    /// <see cref="ObjectiveType.IceCellsCleared"/> objective with an
    /// <see cref="ObjectiveType.DiamondsCleared"/> one. With this exclusion in place, every ice socket
    /// is a position a decorated piece's diamond can never land on, so a board with many sockets eats
    /// into the positions the diamond decoration can ever be collected from and could starve that
    /// objective. The same note is left on <c>TargetIceCellAuthoring</c> for whoever authors the level.
    /// </para>
    /// </summary>
    public static class DiamondCellRules
    {
        /// <summary>
        /// True when a decorated piece's cell landing on <paramref name="position"/> may become a
        /// <see cref="SpecialCellKind.Diamond"/> cell. False for a cell that already carries any
        /// <see cref="SpecialCellKind"/> (a timer cell most of all), any reinforcement hits, or any ice
        /// still to melt (<see cref="Board.GetIceLevel"/>), in which case the cell is placed as an
        /// ordinary block of the piece's colour instead.
        /// </summary>
        public static bool CanCarryDiamond(Board board, GridPosition position)
        {
            return board.GetSpecialKind(position) == SpecialCellKind.None
                && board.GetHitCount(position) == 0
                && board.GetIceLevel(position) == 0;
        }
    }
}
