using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// <see cref="SpecialCellKind.ExplosiveCore"/>'s effect: destroying one finishes off every row and
    /// column that is missing exactly one occupied playable cell — a "finish the job" tool rather than
    /// an area blast.
    /// <para>
    /// <b>It never clears a line itself.</b> Exactly as <see cref="VortexEffect"/>'s pull and
    /// <see cref="ChainLightningEffect"/>'s strike deliberately leave a completed line standing for
    /// <see cref="CascadeClearResolver"/> to notice on its next iteration, this effect only occupies the
    /// one missing cell of a qualifying row/column. The line is now genuinely full, so the resolver's
    /// own re-check clears it the ordinary way on the very next iteration — the same code path a
    /// placement's own clear uses, which is what lets a core caught inside that clear re-trigger its own
    /// effect through the resolver's existing "every special cell a phase destroys fires its effect"
    /// mechanism, with no plumbing added here for it.
    /// </para>
    /// <para>
    /// <b>Hand-off.</b> When nothing on the board is one cell short, the core's job cannot be done this
    /// detonation, so its kind is transferred instead of wasted: a uniformly random currently-occupied
    /// cell carrying no special kind of its own becomes the new <see cref="SpecialCellKind.ExplosiveCore"/>.
    /// A board with no eligible cell (every occupied cell already special, or nothing occupied at all)
    /// simply loses the core — there is nowhere left to hand it to.
    /// </para>
    /// <para>
    /// Long-lived by design, exactly as <see cref="LaserEffect"/> and <see cref="VortexEffect"/> are:
    /// one instance is owned by each System that resolves clears, and <see cref="BeginResolution"/> is
    /// called before each resolution rather than a fresh instance being allocated.
    /// </para>
    /// </summary>
    public sealed class ExplosiveCoreEffect : ISpecialCellEffect
    {
        private readonly List<int> _qualifyingRows = new List<int>(Board.SIZE);
        private readonly List<int> _qualifyingColumns = new List<int>(Board.SIZE);

        /// <summary>Every currently-occupied, not-already-special cell found while looking for a
        /// hand-off target. Refilled and consumed within one <see cref="TryHandOff"/> call.</summary>
        private readonly List<GridPosition> _handOffCandidates = new List<GridPosition>(Board.SIZE * Board.SIZE);

        /// <summary>Every cell this effect handed <see cref="SpecialCellKind.ExplosiveCore"/> to since
        /// the last <see cref="BeginResolution"/>, in the order it handed them off.</summary>
        private readonly List<GridPosition> _handOffTargets = new List<GridPosition>(4);

        private readonly Random _random;

        /// <summary>
        /// The random stream a hand-off target is drawn from, supplied rather than created so the
        /// System that owns the run owns its randomness — one seeded stream per run makes a run
        /// reproducible, which a stream this class created for itself never could.
        /// </summary>
        public ExplosiveCoreEffect(Random random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>How many rows and columns this effect finished (filled full) since the last
        /// <see cref="BeginResolution"/> — summed across every <see cref="Apply"/> call in the
        /// resolution, exactly as a placement's own <c>LineCount</c> would be. What the finished lines
        /// actually destroy is reported by the cascade's own next phase, not by this effect: filling the
        /// one missing cell is all this effect ever does to the board.</summary>
        public int FinishedLineCount { get; private set; }

        /// <summary>Every cell this effect handed <see cref="SpecialCellKind.ExplosiveCore"/> to since
        /// the last <see cref="BeginResolution"/>. Empty unless a detonation found nothing to finish.</summary>
        public IReadOnlyList<GridPosition> HandOffTargets => _handOffTargets;

        /// <summary>Starts a new resolution: forgets the previous one's counts. Must be called before
        /// the resolution that will apply this effect, or the two resolutions' totals would be reported
        /// as one.</summary>
        public void BeginResolution()
        {
            FinishedLineCount = 0;
            _handOffTargets.Clear();
        }

        /// <summary>
        /// Finishes every row/column missing exactly one occupied playable cell, or — when none
        /// qualifies — hands the core's kind off to a random occupied, not-yet-special cell.
        /// <para>
        /// The trigger's own cell is already empty when this is called (see
        /// <see cref="ISpecialCellEffect"/>); it plays no other part here; unlike the blast this
        /// replaces, the core's own former position is not a centre to act around.
        /// </para>
        /// </summary>
        public void Apply(Board board, SpecialCellTrigger trigger)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (trigger.Kind != SpecialCellKind.ExplosiveCore || !board.IsInside(trigger.Position))
            {
                return;
            }

            _qualifyingRows.Clear();
            _qualifyingColumns.Clear();

            for (int y = 0; y < board.Height; y++)
            {
                if (board.IsRowOneCellFromFull(y))
                {
                    _qualifyingRows.Add(y);
                }
            }

            for (int x = 0; x < board.Width; x++)
            {
                if (board.IsColumnOneCellFromFull(x))
                {
                    _qualifyingColumns.Add(x);
                }
            }

            if (_qualifyingRows.Count == 0 && _qualifyingColumns.Count == 0)
            {
                TryHandOff(board);
                return;
            }

            for (int i = 0; i < _qualifyingRows.Count; i++)
            {
                FillRowGap(board, _qualifyingRows[i]);
            }

            for (int i = 0; i < _qualifyingColumns.Count; i++)
            {
                FillColumnGap(board, _qualifyingColumns[i]);
            }

            FinishedLineCount += _qualifyingRows.Count + _qualifyingColumns.Count;
        }

        /// <summary>
        /// Occupies row <paramref name="y"/>'s one missing playable cell, completing it.
        /// <para>
        /// Reads a fallback colour from any occupied cell already in the row while it looks for the
        /// gap, so the finished line matches whatever the player was already building rather than an
        /// arbitrary id — and falls back to the palette's first colour on the one row shape that can
        /// qualify with zero occupied cells (a single-playable-cell row that is entirely empty).
        /// </para>
        /// <para>
        /// A no-op if the gap turns out already filled: a row and a column that share their one missing
        /// cell are both queued from the same, unmutated scan, so the second fill to run finds nothing
        /// left to do — which is exactly the "both cleared" outcome a shared gap is supposed to produce.
        /// </para>
        /// </summary>
        private static void FillRowGap(Board board, int y)
        {
            GridPosition gap = default;
            bool foundGap = false;
            int referenceColour = Board.EMPTY;

            for (int x = 0; x < board.Width; x++)
            {
                var position = new GridPosition(x, y);
                if (board.IsHole(position))
                {
                    continue;
                }

                if (board.IsOccupied(position))
                {
                    referenceColour = board[position];
                }
                else
                {
                    gap = position;
                    foundGap = true;
                }
            }

            if (!foundGap)
            {
                return;
            }

            board.Occupy(gap, ResolveFillColour(referenceColour));
        }

        /// <summary>Column counterpart of <see cref="FillRowGap"/>. Same rule, same fallback, same
        /// already-filled no-op.</summary>
        private static void FillColumnGap(Board board, int x)
        {
            GridPosition gap = default;
            bool foundGap = false;
            int referenceColour = Board.EMPTY;

            for (int y = 0; y < board.Height; y++)
            {
                var position = new GridPosition(x, y);
                if (board.IsHole(position))
                {
                    continue;
                }

                if (board.IsOccupied(position))
                {
                    referenceColour = board[position];
                }
                else
                {
                    gap = position;
                    foundGap = true;
                }
            }

            if (!foundGap)
            {
                return;
            }

            board.Occupy(gap, ResolveFillColour(referenceColour));
        }

        /// <summary>The colour a filled gap takes: whatever colour the line already carried, or the
        /// palette's first colour when the line held nothing to copy from. The fill is cleared again on
        /// the very next cascade iteration, so this is cosmetic only — it never affects which lines
        /// clear or what they score.</summary>
        private static int ResolveFillColour(int referenceColour)
            => referenceColour == Board.EMPTY ? 1 : referenceColour;

        /// <summary>
        /// Transfers <see cref="SpecialCellKind.ExplosiveCore"/> onto a uniformly random occupied cell
        /// carrying no special kind of its own. A no-op when no such cell exists — the core is simply
        /// lost, exactly as a Vortex with nothing isolated to pull moves nothing.
        /// </summary>
        private void TryHandOff(Board board)
        {
            _handOffCandidates.Clear();

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    if (board.IsOccupied(position) && board.GetSpecialKind(position) == SpecialCellKind.None)
                    {
                        _handOffCandidates.Add(position);
                    }
                }
            }

            if (_handOffCandidates.Count == 0)
            {
                return;
            }

            GridPosition target = _handOffCandidates[_random.Next(_handOffCandidates.Count)];
            board.SetSpecialKind(target, SpecialCellKind.ExplosiveCore);
            _handOffTargets.Add(target);
        }
    }
}
