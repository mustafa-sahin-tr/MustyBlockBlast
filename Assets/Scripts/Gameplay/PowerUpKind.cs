namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// The three board-mutating power-ups. Each is earned separately and kept in its own inventory
    /// slot, so the kind is the identity used by persistence, messaging and scoring alike.
    /// </summary>
    public enum PowerUpKind
    {
        /// <summary>Clears the 3x3 area around the targeted cell.</summary>
        Bomb,

        /// <summary>Clears a whole row, full or not.</summary>
        RowClear,

        /// <summary>Clears a whole column, full or not.</summary>
        ColumnClear,
    }
}
