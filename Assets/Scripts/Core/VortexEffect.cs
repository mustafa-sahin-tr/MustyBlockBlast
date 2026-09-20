using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// <see cref="SpecialCellKind.Vortex"/>'s effect: destroying one fills every fully-enclosed pocket
    /// ("island") of empty cells the board has at that moment — see
    /// <see cref="Board.CollectEnclosedEmptyIslands"/> for what counts as one — reclaiming dead space the
    /// player could otherwise never clear (issue #349).
    /// <para>
    /// <b>Replaces the earlier "drag isolated blocks inwards" behaviour outright.</b> The kind keeps its
    /// name, id and spawn rule (<see cref="VortexSpawnSelector"/> is untouched by this rewrite); only
    /// what happens on destruction changed.
    /// </para>
    /// <para>
    /// <b>Hand-off.</b> When the board has no island at all, the tag is transferred instead: a uniformly
    /// random currently-occupied cell that carries no <see cref="SpecialCellKind"/> of its own is chosen
    /// and tagged <see cref="SpecialCellKind.Vortex"/>, so the ability is not simply lost on a board that
    /// happens to have nothing to reclaim. A board with no eligible cell either — nothing occupied, or
    /// every occupied cell already special — hands off to nothing at all, which is a no-op exactly as
    /// every other special cell's "found no target" case is.
    /// </para>
    /// <para>
    /// <b>Colouring a filled cell</b> draws uniformly from 1..<see cref="Board.COLOUR_COUNT"/>, the same
    /// range an ordinary piece's colour is drawn from: colour is cosmetic and never affects placement or
    /// clearing, so a reclaimed cell needs no rule of its own.
    /// </para>
    /// <para>
    /// <b>Never overwrites a Reinforced cell.</b> A fill only ever targets cells
    /// <see cref="Board.CollectEnclosedEmptyIslands"/> reports, which are empty by construction, and a
    /// hit count belongs to the block standing on a cell — an empty one has none.
    /// </para>
    /// <para>
    /// <b>Never clears a line itself</b>, even one a fill happens to complete —
    /// <see cref="CascadeClearResolver"/> re-checks fullness on the next iteration, which is how a fill
    /// that completes a line chains, exactly as every other effect's fill/destroy does.
    /// </para>
    /// <para>
    /// Long-lived by design, exactly as <see cref="ExplosiveCoreEffect"/>, <see cref="LaserEffect"/> and
    /// <see cref="ChainLightningEffect"/> are: one instance per System that resolves clears, with
    /// <see cref="BeginResolution"/> called before each resolution rather than a fresh instance
    /// allocated. <see cref="FilledCells"/> and <see cref="HandOffTargets"/> are therefore buffers this
    /// instance owns and overwrites — a caller that needs either beyond the current resolution must copy
    /// them.
    /// </para>
    /// </summary>
    public sealed class VortexEffect : ISpecialCellEffect
    {
        /// <summary>
        /// The random stream a fill's colour and a hand-off's target are drawn from, supplied rather
        /// than created so the System that owns the run owns its randomness — exactly as
        /// <see cref="ChainLightningEffect"/> takes one, and for the same reason: one seeded stream per
        /// run makes a run reproducible, which a stream this class created for itself could never be.
        /// </summary>
        private readonly Random _random;

        /// <summary>Every cell filled since the last <see cref="BeginResolution"/>, in the order the scan
        /// found them, across every vortex destroyed in that resolution.</summary>
        private readonly List<GridPosition> _filledCells = new List<GridPosition>(Board.SIZE * Board.SIZE);

        /// <summary>Every cell a hand-off tagged since the last <see cref="BeginResolution"/>. Almost
        /// always at most one — a resolution hands off more than once only when several vortices were
        /// destroyed and each in turn found the board already clear of islands.</summary>
        private readonly List<GridPosition> _handOffTargets = new List<GridPosition>(4);

        /// <summary>Scratch buffer for one <see cref="Apply"/> call's island scan. Cleared and refilled
        /// on every call, so one buffer serves every vortex in a resolution.</summary>
        private readonly List<GridPosition> _islandBuffer = new List<GridPosition>(Board.SIZE * Board.SIZE);

        public VortexEffect(Random random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>Every cell a fill reclaimed since the last <see cref="BeginResolution"/>. Empty for a
        /// trigger that found no island — that case writes to <see cref="HandOffTargets"/> instead, never
        /// here.</summary>
        public IReadOnlyList<GridPosition> FilledCells => _filledCells;

        /// <summary>Every cell a hand-off tagged <see cref="SpecialCellKind.Vortex"/> since the last
        /// <see cref="BeginResolution"/>. Empty for a trigger that found at least one island and filled it
        /// instead.</summary>
        public IReadOnlyList<GridPosition> HandOffTargets => _handOffTargets;

        /// <summary>Starts a new resolution: forgets the previous one's fills and hand-offs. Must be
        /// called before the resolution that will apply this effect, or the two resolutions' work would
        /// be reported as one and Presentation would replay an animation that already finished.</summary>
        public void BeginResolution()
        {
            _filledCells.Clear();
            _handOffTargets.Clear();
        }

        /// <summary>
        /// Fills every island the board currently has, or — when it has none — hands the vortex tag to a
        /// random eligible cell.
        /// <para>
        /// The trigger's own cell is already empty when this is called (see
        /// <see cref="ISpecialCellEffect"/>), so it can itself be part of an island the scan finds (it
        /// was just vacated, after all) or, when nothing is enclosed, it is simply one more empty cell —
        /// never a hand-off candidate, since <see cref="SelectHandOffTarget"/> only ever considers
        /// occupied cells.
        /// </para>
        /// </summary>
        public void Apply(Board board, SpecialCellTrigger trigger)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (trigger.Kind != SpecialCellKind.Vortex || !board.IsInside(trigger.Position))
            {
                return;
            }

            _islandBuffer.Clear();
            board.CollectEnclosedEmptyIslands(_islandBuffer);

            if (_islandBuffer.Count > 0)
            {
                for (int i = 0; i < _islandBuffer.Count; i++)
                {
                    GridPosition cell = _islandBuffer[i];
                    board.Occupy(cell, DrawColourId());
                    _filledCells.Add(cell);
                }

                return;
            }

            GridPosition? handOff = SelectHandOffTarget(board);
            if (handOff == null)
            {
                return;
            }

            board.SetSpecialKind(handOff.Value, SpecialCellKind.Vortex);
            _handOffTargets.Add(handOff.Value);
        }

        /// <summary>A uniform 1..<see cref="Board.COLOUR_COUNT"/> draw — the same range an ordinary
        /// piece's colour comes from. Colour is cosmetic and never affects placement or clearing, so a
        /// reclaimed cell needs no rule of its own.</summary>
        private int DrawColourId() => 1 + _random.Next(Board.COLOUR_COUNT);

        /// <summary>
        /// One occupied cell with no <see cref="SpecialCellKind"/> of its own, chosen uniformly, or null
        /// when none exists — a board with nothing occupied, or where every occupied cell already carries
        /// a kind, hands off to nothing (issue #349 AC3/AC6).
        /// <para>
        /// Counts the candidates and then walks to the chosen one, rather than collecting them into a
        /// list — the board is small, cheap to visit twice, and it keeps the pick allocation-free.
        /// Shaped like <see cref="ScoreGemSpawnSelector.SelectSpawnPosition"/>, except that selector's
        /// reward is allowed to land on an already-special cell and this is not: a hand-off cell must be
        /// free to become the vortex, not something else's already.
        /// </para>
        /// </summary>
        private GridPosition? SelectHandOffTarget(Board board)
        {
            int candidateCount = 0;
            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    if (IsEligibleHandOffTarget(board, new GridPosition(x, y)))
                    {
                        candidateCount++;
                    }
                }
            }

            if (candidateCount == 0)
            {
                return null;
            }

            int chosen = _random.Next(candidateCount);
            int seen = 0;

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var candidate = new GridPosition(x, y);
                    if (!IsEligibleHandOffTarget(board, candidate))
                    {
                        continue;
                    }

                    if (seen == chosen)
                    {
                        return candidate;
                    }

                    seen++;
                }
            }

            return null;
        }

        private static bool IsEligibleHandOffTarget(Board board, GridPosition position)
            => board.IsOccupied(position) && board.GetSpecialKind(position) == SpecialCellKind.None;
    }
}
