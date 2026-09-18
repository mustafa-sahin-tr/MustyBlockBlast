using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// Counts placements that clear a row and a column at the same time — the same trigger
    /// <c>BoardSystem.TrySpawnExplosiveCore</c> reacts to — and hands out a
    /// <c>SpecialCellKind.ScoreGem</c> every <see cref="CROSS_CLEARS_PER_GEM"/>th one, then starts
    /// counting again from zero.
    /// <para>
    /// Deliberately not streak-gated, unlike Laser/Coin/Golden: the two cross-clears counted towards one
    /// gem do not have to be consecutive, so an ordinary placement in between — one that clears nothing,
    /// or only a row, or only a column — never costs the player progress they already earned. Easier to
    /// earn on purpose (issue: "score gem art arda olmasına gerek yok").
    /// </para>
    /// <para>
    /// Reactive rather than a plain int purely so this is ordinary, observable run state like every other
    /// Model: it is state a future one-step-undo has to snapshot and restore alongside the board and the
    /// score, and keeping it here rather than as private bookkeeping inside a System is what makes that
    /// possible at all.
    /// </para>
    /// </summary>
    public sealed class ScoreGemProgressModel
    {
        private const int CROSS_CLEARS_PER_GEM = 2;

        /// <summary>Cross-clears recorded since the last gem this run (or since the run started).</summary>
        public ReactiveProperty<int> CrossClearCount { get; } = new ReactiveProperty<int>(0);

        /// <summary>
        /// Records one row+column cross-clear. Returns true — and resets the count back to zero — on
        /// exactly the placement that reaches <see cref="CROSS_CLEARS_PER_GEM"/>; every placement before
        /// that returns false and simply advances the count.
        /// </summary>
        internal bool RecordCrossClear()
        {
            CrossClearCount.Value += 1;
            if (CrossClearCount.Value < CROSS_CLEARS_PER_GEM)
            {
                return false;
            }

            CrossClearCount.Value = 0;
            return true;
        }

        /// <summary>Starts a fresh run with no progress carried over from the last one.</summary>
        internal void Reset() => CrossClearCount.Value = 0;
    }
}
