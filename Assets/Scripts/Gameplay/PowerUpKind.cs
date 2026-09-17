namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// The power-ups. Each is earned separately and kept in its own inventory slot, so the kind is the
    /// identity used by persistence, messaging and scoring alike. The first five mutate the board;
    /// <see cref="Rotate"/> and <see cref="Reroll"/> act on the tray instead, and
    /// <see cref="DoubleMultiplier"/> and <see cref="GhostFit"/> touch neither.
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

        /// <summary>
        /// Discards all three dock pieces and draws three new ones, guaranteeing at least one of them
        /// has a legal placement on the current board — the one draw in the game that is solvability-
        /// guaranteed; ordinary refills stay unguaranteed. Like <see cref="Rotate"/> it touches no cell,
        /// and unlike every other kind it has no target at all: it is applied the moment it is tapped
        /// rather than armed and aimed.
        /// </summary>
        Reroll,

        /// <summary>
        /// Opens a fixed-length window during which every score gain — placements and power-up clears
        /// alike — is worth double. Like <see cref="Reroll"/> it has no target and is applied on the tap
        /// that selects it, and like <see cref="Rotate"/> and <see cref="Reroll"/> it touches no cell.
        /// It is the only kind that changes nothing at the moment it is spent: its whole effect is what
        /// happens for the next few seconds (see <c>DoubleMultiplierModel</c>).
        /// </summary>
        DoubleMultiplier,

        /// <summary>
        /// Searches every dock piece against every board anchor and points at the best move it finds,
        /// projecting a pulsing silhouette on the target cells and pulsing the dock piece that belongs
        /// there. Targetless like <see cref="Reroll"/> and <see cref="DoubleMultiplier"/>, and like them
        /// it touches no cell — but it is the only kind that changes no game state whatsoever: its whole
        /// effect is a suggestion on screen (see <c>GhostFitModel</c>), which the player is free to
        /// ignore. Refused, and charged nothing, when there is no legal placement to point at.
        /// </summary>
        GhostFit,

        /// <summary>
        /// Sows extra <see cref="MustyBlockBlast.Core.SpecialCellKind.Coin"/> cells into a level, bought
        /// by the unit before that level starts: one purchased unit is one coin cell painted onto an
        /// occupied cell at the opening of the run (see <c>LevelCoinCellSeedSystem</c>, which places the
        /// level's own authored cells through the same seam).
        /// <para>
        /// The only kind with no in-run lifecycle whatsoever. Every other kind — even the three
        /// targetless ones — is selected and applied from the HUD during a run; this one is bought and
        /// committed at a level-start screen and has nothing to select, aim or apply, so it is never
        /// armed and appears in neither the power-up strip nor the general shop. Its whole interface is
        /// <c>PowerUpSystem.TrySpendCoinSowerBulk</c>, which pays for a quantity in one go.
        /// </para>
        /// </summary>
        CoinSower,

        /// <summary>
        /// Parks a dock piece into the Hold slot ("pocket"), swapping it with whatever piece is
        /// already parked there. Acts on the tray rather than the board, like <see cref="Rotate"/>
        /// and <see cref="Reroll"/>: nothing clears and nothing scores.
        /// <para>
        /// The only kind invoked by a drag — a tray piece dropped onto the pocket — rather than by
        /// arming from the strip and tapping a target, so it is never armed and lives in its own
        /// slot beside the tray rather than in the power-up strip. Its whole interface is
        /// <c>PowerUpSystem.TryApplyHold</c>.
        /// </para>
        /// <para>
        /// Parking into an empty pocket costs one charge, and so does swapping a new piece into an
        /// occupied one: that swap is the only way a parked piece ever comes back, so gating it the
        /// same way is what keeps a zero-charge pocket from being free to empty. Holding none refuses
        /// both, which can leave an already-parked piece stuck until a charge is earned — the
        /// game-over check reads the count for exactly that reason.
        /// </para>
        /// </summary>
        Hold,
    }
}
