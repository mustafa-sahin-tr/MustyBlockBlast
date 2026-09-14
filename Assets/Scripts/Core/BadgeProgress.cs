using System;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Live progress of one <see cref="BadgeDefinition"/>. Much simpler than
    /// <see cref="ObjectiveProgress"/>: there is no "does this placement qualify" rule engine, because
    /// a badge only ever asks whether a lifetime counter has reached its threshold.
    /// <para>
    /// A badge is a one-way latch. Once unlocked it stays unlocked and never re-fires, which is what
    /// lets the owning System treat <see cref="Evaluate"/>'s return value as "pay the reward now,
    /// exactly once".
    /// </para>
    /// </summary>
    public sealed class BadgeProgress
    {
        public BadgeProgress(BadgeDefinition definition)
        {
            Definition = definition;
        }

        public BadgeDefinition Definition { get; }

        /// <summary>Latest observed counter value, clamped to the threshold so the "x / y" readout can
        /// never show more than the goal — the same display sanity <see cref="ObjectiveProgress"/> keeps.</summary>
        public long CurrentValue { get; private set; }

        public bool IsUnlocked { get; private set; }

        /// <summary>
        /// Folds the latest lifetime total for <see cref="BadgeDefinition.StatType"/> into this badge.
        /// Returns true only on the false-to-true unlock transition, so a caller can grant the reward
        /// straight off the return value without any "have I already paid this" bookkeeping. An
        /// already-unlocked badge always returns false.
        /// </summary>
        public bool Evaluate(long currentStatTotal)
        {
            CurrentValue = Clamp(currentStatTotal);

            if (IsUnlocked || CurrentValue < Definition.Threshold)
            {
                return false;
            }

            IsUnlocked = true;
            return true;
        }

        /// <summary>
        /// Rehydrates a badge that was already unlocked in an earlier session. Deliberately silent and
        /// separate from <see cref="Evaluate"/>: nothing was achieved this session, so restoring must
        /// never look like a fresh unlock and must never pay the reward a second time.
        /// </summary>
        public void RestoreUnlocked()
        {
            IsUnlocked = true;
            CurrentValue = Definition.Threshold;
        }

        private long Clamp(long value) => Math.Min(Math.Max(value, 0L), Definition.Threshold);
    }
}
