namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The extra behaviour one board cell carries on top of its cosmetic colour id. Strictly
    /// orthogonal to occupancy and to colour: a kind only says what happens when that cell is
    /// <em>destroyed</em>, never whether the cell is occupied or which colour it shows — so
    /// <see cref="Board.OccupiedCellCount"/>, <see cref="Board.IsEmpty"/>, line fullness and every
    /// flood-fill stay purely occupancy-based and are unaffected by anything on this enum.
    /// <para>
    /// Only <see cref="None"/> exists today. Every cell is therefore a no-op, which is what lets the
    /// cascading resolution loop (<see cref="CascadeClearResolver"/>) ship with zero player-facing
    /// behaviour change; the real kinds arrive one per sub-issue of the Special Cells epic.
    /// </para>
    /// <para>
    /// A kind is stored per cell and copied by <see cref="Board.Clone"/>, so it is part of any board
    /// snapshot: members may be appended but never reordered or renamed.
    /// </para>
    /// </summary>
    public enum SpecialCellKind
    {
        /// <summary>An ordinary cell. Destroying it empties it and does nothing else.</summary>
        None = 0,
    }
}
