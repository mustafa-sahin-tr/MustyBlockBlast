using System;
using MustyBlockBlast.Gameplay;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Pins the authored gate table. These numbers are a design decision, not an implementation detail:
    /// changing one changes when a player meets a power-up for the first time, so it must not be
    /// possible to change one by accident.
    /// </summary>
    public class PowerUpUnlockLevelsTests
    {
        /// <summary>The starter three, and Hold: available from a fresh install, before any level is
        /// cleared. Hold is ungated deliberately (issue #202) — it meters a pocket that was on screen
        /// from the first run before it became a power-up, so rationing its charges must not also
        /// take the pocket away.</summary>
        [TestCase(PowerUpKind.Bomb)]
        [TestCase(PowerUpKind.RowClear)]
        [TestCase(PowerUpKind.ColumnClear)]
        [TestCase(PowerUpKind.Hold)]
        public void LevelFor_TheStarterKinds_IsAlwaysUnlocked(PowerUpKind kind)
        {
            Assert.AreEqual(PowerUpUnlockLevels.ALWAYS_UNLOCKED, PowerUpUnlockLevels.LevelFor(kind));
        }

        /// <summary>The seven gated kinds, one every five levels, in the authored order.</summary>
        [TestCase(PowerUpKind.Joker, 5)]
        [TestCase(PowerUpKind.ColorCleanser, 10)]
        [TestCase(PowerUpKind.Rotate, 15)]
        [TestCase(PowerUpKind.Reroll, 20)]
        [TestCase(PowerUpKind.DoubleMultiplier, 25)]
        [TestCase(PowerUpKind.GhostFit, 30)]
        [TestCase(PowerUpKind.CoinSower, 35)]
        public void LevelFor_TheGatedKinds_MatchesTheAuthoredCadence(PowerUpKind kind, int expectedLevel)
        {
            Assert.AreEqual(expectedLevel, PowerUpUnlockLevels.LevelFor(kind));
        }

        /// <summary>
        /// A new kind must be given a gate deliberately. Without this, appending to
        /// <see cref="PowerUpKind"/> silently inherits the switch's default — "unlocked from the
        /// start" — which is the one answer nobody would have chosen on purpose for a tenth power-up.
        /// </summary>
        [Test]
        public void EveryKind_IsCoveredByTheTableTestsAbove()
        {
            // The exact set, not just the count: replacing one kind with another keeps the count the
            // same, and the replacement would inherit the default silently.
            var covered = new[]
            {
                PowerUpKind.Bomb,
                PowerUpKind.RowClear,
                PowerUpKind.ColumnClear,
                PowerUpKind.Joker,
                PowerUpKind.ColorCleanser,
                PowerUpKind.Rotate,
                PowerUpKind.Reroll,
                PowerUpKind.DoubleMultiplier,
                PowerUpKind.GhostFit,
                PowerUpKind.CoinSower,
                PowerUpKind.Hold,
            };

            CollectionAssert.AreEquivalent(
                covered,
                (PowerUpKind[])Enum.GetValues(typeof(PowerUpKind)),
                "A kind was added, removed or replaced. Give it a gate level and a case above.");
        }

        /// <summary>A fresh install sits at level 1, so only the starter three and Hold may be open there.</summary>
        [Test]
        public void IsUnlockedAt_OnAFreshInstall_OpensExactlyTheStarterKinds()
        {
            const int FRESH_INSTALL_LEVEL = 1;

            Assert.IsTrue(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.Bomb, FRESH_INSTALL_LEVEL));
            Assert.IsTrue(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.RowClear, FRESH_INSTALL_LEVEL));
            Assert.IsTrue(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.ColumnClear, FRESH_INSTALL_LEVEL));
            Assert.IsTrue(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.Hold, FRESH_INSTALL_LEVEL));

            Assert.IsFalse(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.Joker, FRESH_INSTALL_LEVEL));
            Assert.IsFalse(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.ColorCleanser, FRESH_INSTALL_LEVEL));
            Assert.IsFalse(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.Rotate, FRESH_INSTALL_LEVEL));
            Assert.IsFalse(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.Reroll, FRESH_INSTALL_LEVEL));
            Assert.IsFalse(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.DoubleMultiplier, FRESH_INSTALL_LEVEL));
            Assert.IsFalse(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.GhostFit, FRESH_INSTALL_LEVEL));
            Assert.IsFalse(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.CoinSower, FRESH_INSTALL_LEVEL));
        }

        /// <summary>
        /// Issue #167's gate, stated on its own edges: the Coin Sower's level-start offer is closed at 34
        /// and open from 35 on. Pinned separately from the cadence table above because this one is read by
        /// a screen that exists to sell it — a gate quietly moved here would put a purchase in front of a
        /// player who has not met the power-up.
        /// </summary>
        [Test]
        public void IsUnlockedAt_ForCoinSower_OpensAtThirtyFive()
        {
            Assert.IsFalse(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.CoinSower, 1));
            Assert.IsFalse(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.CoinSower, 34));
            Assert.IsTrue(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.CoinSower, 35));
            Assert.IsTrue(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.CoinSower, 36));
        }

        /// <summary>The gate opens on the level itself, not the one after it.</summary>
        [Test]
        public void IsUnlockedAt_OnTheGateLevelItself_IsOpen()
        {
            Assert.IsFalse(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.Rotate, 14));
            Assert.IsTrue(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.Rotate, 15));
            Assert.IsTrue(PowerUpUnlockLevels.IsUnlockedAt(PowerUpKind.Rotate, 16));
        }

        /// <summary>Past the last gate everything is open, and stays open.</summary>
        [Test]
        public void IsUnlockedAt_PastTheLastGate_OpensEveryKind()
        {
            foreach (PowerUpKind kind in (PowerUpKind[])Enum.GetValues(typeof(PowerUpKind)))
            {
                Assert.IsTrue(PowerUpUnlockLevels.IsUnlockedAt(kind, 35), kind.ToString());
            }
        }
    }
}
