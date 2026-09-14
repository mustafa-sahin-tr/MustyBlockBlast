namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// The power-ups. Each is earned separately and kept in its own inventory slot, so the kind is the
    /// identity used by persistence, messaging and scoring alike. All but <see cref="Rotate"/> mutate
    /// the board; <see cref="Rotate"/> is the one kind aimed at the tray instead.
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

        /// <summary>
        /// Clears every cell on the board sharing the targeted cell's colour. Like Joker (and unlike
        /// Bomb/RowClear/ColumnClear), an illegal target — here, an empty cell, which has no colour to
        /// extract — is rejected outright: nothing is spent, nothing is disarmed.
        /// </summary>
        ColorCleanser,

        /// <summary>
        /// Turns one dock piece 90 degrees clockwise, swapping its tray slot to the catalog piece that
        /// already describes that orientation. The only kind that never touches the board: it is aimed
        /// at the tray, clears nothing and scores nothing. A fully symmetrical piece (1x1, 2x2, 3x3)
        /// has no distinct rotation and is rejected outright, like Joker's and ColorCleanser's illegal
        /// targets — nothing is spent, nothing is disarmed.
        /// </summary>
        Rotate,
    }
}
