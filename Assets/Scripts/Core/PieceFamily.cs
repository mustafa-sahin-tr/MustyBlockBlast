namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Shape family a piece belongs to, independent of its size or orientation. Objectives are
    /// written against families ("place 10 T-pieces") rather than the individual
    /// <see cref="PieceCatalog"/> ids, so adding an orientation never invalidates a level.
    /// </summary>
    public enum PieceFamily
    {
        Single = 0,
        Line = 1,
        Square = 2,
        Corner = 3,
        TShape = 4,
        SShape = 5,
        ZShape = 6,
    }
}
