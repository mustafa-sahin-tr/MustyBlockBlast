namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The extra behaviour one board cell carries on top of its cosmetic colour id. Strictly
    /// orthogonal to occupancy and to colour: a kind only says what happens when that cell is
    /// <em>destroyed</em>, never whether the cell is occupied or which colour it shows — so
    /// <see cref="Board.OccupiedCellCount"/>, <see cref="Board.IsEmpty"/>, line fullness and every
    /// flood-fill stay purely occupancy-based and are unaffected by anything on this enum.
    /// <para>
    /// The real kinds arrive one per sub-issue of the Special Cells epic; the cascading resolution
    /// loop (<see cref="CascadeClearResolver"/>) is what applies them, through
    /// <see cref="ISpecialCellEffect"/>, and knows nothing about any individual kind.
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

        /// <summary>
        /// An "explosive core": destroying it also destroys the 3x3 area around it, clamped to the
        /// board exactly as the Bomb power-up's footprint is (<see cref="PowerUpTargetCells.ForBomb"/>)
        /// — the two are deliberately the same geometry, so a blast is a blast whatever set it off.
        /// A second explosive core caught in the blast detonates in turn; see
        /// <see cref="ExplosiveCoreEffect"/>, which owns that chain.
        /// </summary>
        ExplosiveCore = 1,

        /// <summary>
        /// A "laser": destroying it wipes the full line running at right angles to whatever destroyed
        /// it — taken out by a row clear it wipes its column, taken out by a column clear it wipes its
        /// row. Destroyed by something with no line to it at all (a Bomb, a Colour Cleanser), there is
        /// no opposite to compute and it wipes both. A second laser caught in a wipe fires in turn; see
        /// <see cref="LaserEffect"/>, which owns that chain.
        /// </summary>
        Laser = 2,

        /// <summary>
        /// A "score gem": the one kind that destroys nothing at all. Destroying it multiplies the
        /// score of whatever destroyed it — the whole event, base and combo alike — by
        /// <see cref="ScoreRules.SCORE_GEM_FACTOR"/>. Because it has no board effect there is
        /// deliberately no <see cref="ISpecialCellEffect"/> implementation that mutates anything for it;
        /// <see cref="ScoreGemEffect"/> exists only to count the gems one resolution destroyed, and the
        /// multiplication itself happens in the scoring Systems.
        /// </summary>
        ScoreGem = 3,
    }
}
