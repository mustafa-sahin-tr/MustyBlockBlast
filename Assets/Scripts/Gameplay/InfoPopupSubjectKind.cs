namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// Which family of on-screen element an <see cref="InfoPopupContent"/> explains. Four subject kinds
    /// can each earn an info popup: a special board cell, a power-up, the Hold slot (a single fixed
    /// subject with no per-kind variant), and a special tray piece.
    /// </summary>
    public enum InfoPopupSubjectKind
    {
        SpecialCell,
        PowerUp,
        Hold,
        SpecialPiece,
    }
}
