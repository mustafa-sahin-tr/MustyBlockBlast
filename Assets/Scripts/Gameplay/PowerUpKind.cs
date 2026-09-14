namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// The four board-mutating power-ups. Each is earned separately and kept in its own inventory
    /// slot, so the kind is the identity used by persistence, messaging and scoring alike.
    /// <para>
    /// Values are persisted by name (see <c>PowerUpInventoryKey</c>), so members may be appended but
    /// never reordered or renamed.
    /// </para>
    /// </summary>
    public enum PowerUpKind
    {
        /// <summary>Clears the 3x3 area around the targeted cell.</summary>
        Bomb,

        /// <summary>Clears a whole row, full or not.</summary>
        RowClear,

        /// <summary>Clears a whole column, full or not.</summary>
        ColumnClear,

        /// <summary>
        /// Fills one empty cell, and clears that cell's row and/or column if the fill completed them.
        /// The odd one out: it adds rather than removes, and unlike the other three it only clears a
        /// line that genuinely became full — exactly as a normal placement does.
        /// </summary>
        Joker,
    }
}
