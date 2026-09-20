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
        /// An "explosive core": destroying it finishes off every row and column on the board that is
        /// missing exactly one occupied playable cell, mirroring how <see cref="Board.IsRowFull"/> and
        /// <see cref="Board.IsColumnFull"/> already treat holes. When nothing qualifies, the kind is
        /// handed off instead of wasted — transferred to a uniformly random occupied cell carrying no
        /// special kind of its own, or lost outright when no such cell exists. A second explosive core
        /// caught in a line this one finishes detonates in turn, through
        /// <see cref="CascadeClearResolver"/>'s ordinary "every special cell a phase destroys fires its
        /// effect" mechanism; see <see cref="ExplosiveCoreEffect"/>, which owns the scan and the hand-off.
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

        /// <summary>
        /// A "vortex": destroying it fills every fully-enclosed pocket ("island") of empty cells the
        /// board has at that moment — see <see cref="Board.CollectEnclosedEmptyIslands"/> for what
        /// counts as one — reclaiming dead space the player could otherwise never clear (issue #349).
        /// The one kind that creates blocks rather than destroying or moving them, so a board it tidies
        /// holds <em>more</em> occupied cells afterwards, never fewer; see <see cref="VortexEffect"/>,
        /// which owns the scan and the fill. A fill that completes a line clears it through the ordinary
        /// cascade, not through the effect.
        /// <para>
        /// When the board has no island at all, the tag is instead handed off to a uniformly random
        /// occupied cell carrying no <see cref="SpecialCellKind"/> of its own, so the ability survives a
        /// destruction that found nothing to reclaim; a board with no eligible cell either is a no-op.
        /// </para>
        /// <para>
        /// Replaces this kind's original "drag every isolated occupied block one cell inwards" effect
        /// outright — the kind keeps its name, id and spawn rule, only what happens on destruction
        /// changed.
        /// </para>
        /// </summary>
        Vortex = 4,

        /// <summary>
        /// A "chain lightning": destroying it arcs to up to
        /// <see cref="ChainLightningEffect.MAX_TARGETS_PER_STRIKE"/> occupied cells drawn at random from
        /// anywhere on the board — never a neighbourhood, never a line — and vaporizes them where they
        /// stand. The one kind whose footprint cannot be read off the board before it fires: every
        /// other kind's is geometry, and this one's is a sample, bounded by however many cells are
        /// actually occupied. A second chain lightning caught in a strike fires in turn; see
        /// <see cref="ChainLightningEffect"/>, which owns that chain. Earned by the one move that is
        /// hard to make on a crowded board — landing a 3x3 square or a 1x5 bar that also clears a line
        /// (see <see cref="ChainLightningSpawnSelector"/>).
        /// </summary>
        ChainLightning = 5,

        /// <summary>
        /// A "coin": the second kind that destroys nothing at all. Destroying it pays a fixed number of
        /// coins into the player's wallet, doubled when it was destroyed at <see cref="ClearAxis.Both"/>
        /// — the intersection of a row and a column closed at once, the one destruction the player had to
        /// set up twice over. Because the payout is off-board there is deliberately no
        /// <see cref="ISpecialCellEffect"/> implementation that mutates anything for it;
        /// <see cref="CoinEffect"/> exists only to total what one resolution's coins are worth, and the
        /// crediting itself happens in the currency Systems.
        /// <para>
        /// Unlike <see cref="ScoreGem"/>, which multiplies a run-bound score, this pays into a balance
        /// that outlives the run — so every instance pays every time it is destroyed, and a coin cell
        /// left standing when the run ends pays nothing at all.
        /// </para>
        /// </summary>
        Coin = 6,

        /// <summary>
        /// A "timer block": carries its own remaining countdown, measured in placements rather than
        /// wall-clock seconds and decremented by exactly one on every successful placement anywhere on
        /// the board (issue #307 AC2/AC9) — a global per-placement tick, not a per-hit or per-line one.
        /// <para>
        /// Destroyed the ordinary way — a completed row/column, a power-up clear, a special cell's own
        /// blast/wipe/strike — before its countdown reaches zero, it is removed with no side effect
        /// beyond whatever "cleared" already does for an ordinary cell: no explosion, no bonus, no
        /// penalty. There is deliberately no <see cref="ISpecialCellEffect"/> implementation that
        /// mutates anything for it, mirroring <see cref="ScoreGem"/> and <see cref="Coin"/>;
        /// <see cref="TimerCellClearEffect"/> exists only to count the ones a resolution destroyed in
        /// time, for the objective that credits exactly that (see <c>ObjectiveType.TimerCellsMeltedInTime</c>).
        /// </para>
        /// <para>
        /// When the countdown reaches zero while the cell is still standing, it silently converts to an
        /// ordinary cell — <see cref="None"/> — keeping its occupied, coloured block: no lock, no board
        /// damage. In Endless/Timed mode the run simply continues, minus that one cell's objective
        /// credit. In <see cref="MustyBlockBlast.Gameplay.GameMode.Path"/> a single expiry ends the run
        /// immediately as a failure (<c>GameOverReason.ObjectiveMissed</c>), unconditionally — see
        /// <see cref="MustyBlockBlast.Gameplay.Systems.BoardSystem.TryPlacePiece"/>, which runs the tick.
        /// </para>
        /// <para>
        /// The countdown itself is stored in its own per-cell array on <see cref="Board"/> — deliberately
        /// not <c>_hitCounts</c>, which means "hits remaining" for a Reinforced cell, a different number
        /// entirely — copied by <see cref="Board.Clone"/>/<see cref="Board.CopyFrom"/> exactly as this
        /// kind and the hit counts are, so Undo's full-snapshot restore rewinds it too. Level-authored
        /// only (<c>TimerCellAuthoring</c>/<c>LevelTimerCellSeeder</c>); no power-up ever spawns one
        /// organically.
        /// </para>
        /// </summary>
        Timer = 7,
    }
}
