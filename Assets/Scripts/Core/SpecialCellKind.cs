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
        /// An "explosive core": destroying it wipes the full line running at right angles to whatever
        /// destroyed it — taken out by a row clear it wipes its column, taken out by a column clear it
        /// wipes its row, full or not, edge to edge — and something with no line to it at all (a Bomb, a
        /// Colour Cleanser) wipes both, exactly as <see cref="Laser"/> does since issue #398; every wiped
        /// cell scores a bonus point. A second explosive core caught in a wipe this one causes detonates
        /// in turn; see <see cref="ExplosiveCoreEffect"/>, which owns the scan and the chain. Earned by a
        /// placement that clears a row and a column at once (see
        /// <see cref="ExplosiveCoreSpawnSelector"/>).
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

        /// <summary>
        /// A "diamond": a collectible, and the third kind that destroys nothing at all. Destroying it —
        /// by a completed line, a cascaded phase, a special cell's own blast/wipe/strike, or a spent
        /// power-up — credits exactly one unit to a <c>ObjectiveType.DiamondsCleared</c> objective of the
        /// diamond's own colour, and does nothing else: no board effect, no score bonus (deliberately
        /// NOT the <see cref="ScoreGem"/> path — a diamond is a counter, never a multiplier), no coin.
        /// There is deliberately no <see cref="ISpecialCellEffect"/> implementation that mutates
        /// anything for it, mirroring <see cref="ScoreGem"/>, <see cref="Coin"/> and <see cref="Timer"/>;
        /// <see cref="DiamondClearEffect"/> exists only to count the ones a resolution destroyed, per
        /// colour.
        /// <para>
        /// A diamond's colour is its own attribute, stored in its own per-cell array on <see cref="Board"/>
        /// (<see cref="Board.GetDiamondColourId"/>) and deliberately NOT the block's cosmetic
        /// <c>colourId</c>: the block a diamond rides on may be any colour the piece was drawn in, and the
        /// objective is scoped by the gem, not the block — so a red diamond on a blue block counts for
        /// "clear red diamonds", not for "clear blue cells" beyond what any blue cell already does.
        /// Copied by <see cref="Board.Clone"/>/<see cref="Board.CopyFrom"/> along with the kind, so Undo's
        /// full-snapshot restore brings the diamond back in its own colour (issue #393 AC5).
        /// </para>
        /// <para>
        /// Slice 1 of the Diamond epic (issue #390): this kind, its counting effect and its objective
        /// exist here; how a diamond reaches a piece (#394), how it is drawn (#395) and how a level
        /// authors it (#396) are separate slices. Until those land nothing spawns one outside a test.
        /// </para>
        /// </summary>
        Diamond = 8,

        /// <summary>
        /// A "locked cell" ("Kilitli Hücre", issue #434): a level-authored obstacle that starts a run
        /// pre-occupied and is unlocked <em>indirectly</em> — not by anything happening to it, but by a
        /// required number of its DISTINCT 4-orthogonal neighbours each being destroyed at least once.
        /// The one kind whose whole mechanic is about the cells around it rather than the cell itself.
        /// <para>
        /// While locked it is hole-like to the line-clear rule: <see cref="Board.IsRowFull"/>,
        /// <see cref="Board.IsColumnFull"/>, their one-cell-from-full siblings and
        /// <see cref="Board.CollectRowCells"/>/<see cref="Board.CollectColumnCells"/> all skip it (AC7),
        /// so a row through it can still complete and the completed line never lists the lock among its
        /// destroyed cells. It stays <em>occupied</em> to everything else — which is exactly what keeps
        /// <see cref="PlacementRules.CanPlace"/> refusing it with no guard of its own (AC1).
        /// </para>
        /// <para>
        /// Progress is a 4-bit mask (one bit per direction, see <see cref="Board.GetLockedProgressMask"/>),
        /// never a counter: the same neighbour cleared five times counts once (AC4). Set in exactly one
        /// place — the removal branch of <see cref="Board.TryDamage"/> fans out to the destroyed cell's
        /// neighbours — so every destruction path (line, cascade, power-up, joker, hammer) counts alike.
        /// On reaching the authored threshold (1-3, <c>LockedCellAuthoring</c>) the cell becomes an
        /// ordinary EMPTY cell: no removal effect and no score (AC6/AC8) — but since issue #481 it
        /// "reveals gold": the board records it in <see cref="Board.OpenedLocks"/>, however it opened, and
        /// the resolving System pays it once, at the Coin cell's base payout. There is deliberately no
        /// <see cref="ISpecialCellEffect"/> for it.
        /// </para>
        /// <para>
        /// The mask, the threshold and the randomly assigned visual skin are stored in their own per-cell
        /// arrays on <see cref="Board"/>, reset by <see cref="Board.Clear"/> (they belong to the block,
        /// unlike ice) and copied by <see cref="Board.Clone"/>/<see cref="Board.CopyFrom"/> for Undo (AC10).
        /// Level-authored only (<c>LockedCellAuthoring</c>/<c>LevelLockedCellSeeder</c>).
        /// </para>
        /// </summary>
        Locked = 9,
    }
}
