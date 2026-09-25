using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The burst half of the <see cref="SpecialCellKind.PowerStar"/> rule (issue #482): a destroyed power
    /// star — brought to full charge by a line clear, or destroyed outright by anything else — destroys
    /// every occupied cell in the 3x3 around it. Holes and cells off the board are skipped (AC3), and each
    /// cell goes through <see cref="Board.TryDamage"/>, so a reinforced cell spends a hit and a lock opens
    /// one level exactly as any other destruction would.
    /// <para>
    /// A special cell the burst destroys triggers normally: a second power star bursts in turn (queued, so
    /// a chain of stars never recurses), and every other kind is handed to <see cref="SetChain"/>'s
    /// effect — the owner's composite — as a trigger with no axis, exactly as a Bomb's destruction is.
    /// </para>
    /// <para>
    /// Owned and reused like the other effects: call <see cref="BeginResolution"/> before each resolution;
    /// <see cref="BurstCells"/> then lists every cell this resolution's bursts destroyed, for the owner to
    /// announce.
    /// </para>
    /// </summary>
    public sealed class PowerStarEffect : ISpecialCellEffect
    {
        private const int BURST_RADIUS = 1;

        private readonly List<GridPosition> _burstCells = new List<GridPosition>(Board.SIZE * Board.SIZE);
        private readonly List<GridPosition> _pendingStars = new List<GridPosition>(8);

        private ISpecialCellEffect _chain;
        private bool _bursting;

        /// <summary>Every cell a burst destroyed since the last <see cref="BeginResolution"/>.</summary>
        public IReadOnlyList<GridPosition> BurstCells => _burstCells;

        /// <summary>The effect a burst hands every other special cell it destroys to — the owner's
        /// composite, set once after it is built (it contains this effect, so it cannot be passed in).</summary>
        public void SetChain(ISpecialCellEffect chain) => _chain = chain;

        public void BeginResolution()
        {
            _burstCells.Clear();
            _pendingStars.Clear();
        }

        public void Apply(Board board, SpecialCellTrigger trigger)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (trigger.Kind != SpecialCellKind.PowerStar || !board.IsInside(trigger.Position))
            {
                return;
            }

            _pendingStars.Add(trigger.Position);

            // A star reached through the chain while a burst is already running is picked up by that
            // burst's own loop below, never by a nested one.
            if (_bursting)
            {
                return;
            }

            _bursting = true;
            try
            {
                for (int starIndex = 0; starIndex < _pendingStars.Count; starIndex++)
                {
                    Burst(board, _pendingStars[starIndex]);
                }
            }
            finally
            {
                _pendingStars.Clear();
                _bursting = false;
            }
        }

        private void Burst(Board board, GridPosition centre)
        {
            for (int dy = -BURST_RADIUS; dy <= BURST_RADIUS; dy++)
            {
                for (int dx = -BURST_RADIUS; dx <= BURST_RADIUS; dx++)
                {
                    var cell = new GridPosition(centre.X + dx, centre.Y + dy);
                    if (!board.IsInside(cell) || board.IsHole(cell) || !board.IsOccupied(cell))
                    {
                        continue;
                    }

                    // Read before the damage gate, which wipes them along with the block.
                    SpecialCellKind kind = board.GetSpecialKind(cell);
                    int coinValue = board.GetCoinValue(cell);
                    int diamondColourId = kind == SpecialCellKind.Diamond ? board.GetDiamondColourId(cell) : 0;

                    if (!board.TryDamage(cell))
                    {
                        continue;
                    }

                    _burstCells.Add(cell);

                    if (kind == SpecialCellKind.PowerStar)
                    {
                        _pendingStars.Add(cell);
                    }
                    else if (kind != SpecialCellKind.None && _chain != null)
                    {
                        _chain.Apply(board, new SpecialCellTrigger(cell, kind, ClearAxis.None, coinValue, diamondColourId));
                    }
                }
            }
        }
    }
}
