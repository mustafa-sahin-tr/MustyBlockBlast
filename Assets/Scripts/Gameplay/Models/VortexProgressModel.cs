using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// Counts rows and columns cleared, run-wide, and hands out a <c>SpecialCellKind.Vortex</c> every
    /// <see cref="LINES_PER_VORTEX"/>th line, then starts counting again from zero.
    /// <para>
    /// One placement can contribute more than one line at once — a placement clearing 3 lines followed
    /// later by one clearing 2 reaches the threshold exactly (3 + 2 = 5) on that second placement. There
    /// is no consecutiveness requirement: a placement that clears nothing simply adds zero and leaves
    /// the count where it was, so it can never cost the player progress already earned.
    /// </para>
    /// <para>
    /// Reactive rather than a plain int purely so this is ordinary, observable run state like every
    /// other Model: it is state a future one-step-undo has to snapshot and restore alongside the board
    /// and the score, and keeping it here rather than as private bookkeeping inside a System is what
    /// makes that possible at all.
    /// </para>
    /// </summary>
    public sealed class VortexProgressModel
    {
        private const int LINES_PER_VORTEX = 5;

        /// <summary>Rows plus columns cleared since the last vortex this run (or since the run started).</summary>
        public ReactiveProperty<int> LineClearCount { get; } = new ReactiveProperty<int>(0);

        /// <summary>
        /// Adds <paramref name="lineCount"/> rows/columns to the running total. Returns true — and
        /// resets the total back to zero — exactly on the placement that carries it to
        /// <see cref="LINES_PER_VORTEX"/> or beyond; every placement before that returns false and
        /// simply advances the total. A zero <paramref name="lineCount"/> (nothing cleared) is a no-op.
        /// </summary>
        internal bool RecordLinesCleared(int lineCount)
        {
            if (lineCount <= 0)
            {
                return false;
            }

            LineClearCount.Value += lineCount;
            if (LineClearCount.Value < LINES_PER_VORTEX)
            {
                return false;
            }

            LineClearCount.Value = 0;
            return true;
        }

        /// <summary>Starts a fresh run with no progress carried over from the last one.</summary>
        internal void Reset() => LineClearCount.Value = 0;
    }
}
