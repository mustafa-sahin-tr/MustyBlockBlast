using System;
using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the badge one-way latch: progress tracks the live counter until threshold, the
    /// false-to-true transition fires exactly once, and a restored badge never re-fires — the single
    /// guarantee standing between a badge and a per-launch power-up re-grant.
    /// </summary>
    public class BadgeProgressTests
    {
        private static BadgeProgress Badge(long threshold = 50)
        {
            return new BadgeProgress(new BadgeDefinition("first_steps", BadgeStatType.TotalPiecesPlaced, threshold));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(49)]
        public void Evaluate_BelowThreshold_DoesNotUnlock(long statTotal)
        {
            BadgeProgress badge = Badge(threshold: 50);

            Assert.IsFalse(badge.Evaluate(statTotal));
            Assert.AreEqual(statTotal, badge.CurrentValue);
            Assert.IsFalse(badge.IsUnlocked);
        }

        [Test]
        public void Evaluate_ReachingThreshold_UnlocksOnceOnTheTransition()
        {
            BadgeProgress badge = Badge(threshold: 50);

            Assert.IsFalse(badge.Evaluate(49));
            Assert.IsTrue(badge.Evaluate(50));
            Assert.AreEqual(50, badge.CurrentValue);
            Assert.IsTrue(badge.IsUnlocked);
        }

        [Test]
        public void Evaluate_AfterUnlock_NeverReturnsTrueAgain()
        {
            BadgeProgress badge = Badge(threshold: 50);
            Assert.IsTrue(badge.Evaluate(50));

            // Further qualifying (or over-qualifying) updates must not re-trigger a grant.
            Assert.IsFalse(badge.Evaluate(51));
            Assert.IsFalse(badge.Evaluate(1000));
            Assert.IsTrue(badge.IsUnlocked);
        }

        [Test]
        public void CurrentValue_NeverExceedsThreshold()
        {
            BadgeProgress badge = Badge(threshold: 50);

            badge.Evaluate(1000);

            Assert.AreEqual(50, badge.CurrentValue);
        }

        [Test]
        public void RestoreUnlocked_MarksUnlockedWithoutReturningATransition()
        {
            BadgeProgress badge = Badge(threshold: 50);

            badge.RestoreUnlocked();

            Assert.IsTrue(badge.IsUnlocked);
            Assert.AreEqual(50, badge.CurrentValue);
        }

        [Test]
        public void Evaluate_AfterRestoreUnlocked_NeverGrantsAgain()
        {
            BadgeProgress badge = Badge(threshold: 50);
            badge.RestoreUnlocked();

            // This is the exact boot-time sequence: a persisted-unlocked badge immediately re-evaluated
            // against the freshly-loaded live counter. It must not look like a fresh unlock.
            Assert.IsFalse(badge.Evaluate(50));
            Assert.IsFalse(badge.Evaluate(1000));
            Assert.IsTrue(badge.IsUnlocked);
        }

        [Test]
        public void Evaluate_LowStatAfterRestoreUnlocked_StaysUnlockedButReadoutReflectsRealCounter()
        {
            // Documents a known, harmless edge case: if the persisted counter is lost (e.g. corrupt
            // PlayerPrefs reset to 0) while the unlock blob survives, the readout goes stale-looking but
            // the latch itself must still hold — no re-grant, ever.
            BadgeProgress badge = Badge(threshold: 50);
            badge.RestoreUnlocked();

            Assert.IsFalse(badge.Evaluate(0));
            Assert.AreEqual(0, badge.CurrentValue);
            Assert.IsTrue(badge.IsUnlocked);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Construction_RejectsANonPositiveThreshold_BecauseItWouldUnlockInstantly(long threshold)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BadgeDefinition(
                "bad_threshold", BadgeStatType.TotalPiecesPlaced, threshold));
        }
    }
}
