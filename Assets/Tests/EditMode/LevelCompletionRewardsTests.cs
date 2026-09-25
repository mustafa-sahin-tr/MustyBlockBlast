using System.Collections.Generic;
using MustyBlockBlast.Gameplay;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="LevelCompletionRewards"/>, the issue #462 rule: every level pays, a milestone
    /// pays three different kinds, the kind a clear unlocks is guaranteed, and the whole thing is a
    /// pure function of the level number so the Level Path's preview is the payout.
    /// </summary>
    public class LevelCompletionRewardsTests
    {
        /// <summary>Generous upper bound on the levels the rule is swept across — well past the shipped
        /// catalog and every unlock gate, so a roster change cannot slip past the sweep.</summary>
        private const int SWEEP_LAST_LEVEL = 200;

        // --- Unlock guarantee ---

        [TestCase(4, PowerUpKind.Joker)]
        [TestCase(9, PowerUpKind.ColorCleanser)]
        [TestCase(14, PowerUpKind.Rotate)]
        [TestCase(19, PowerUpKind.Reroll)]
        [TestCase(24, PowerUpKind.DoubleMultiplier)]
        [TestCase(29, PowerUpKind.GhostFit)]
        [TestCase(39, PowerUpKind.PaintCross)]
        public void TheClearThatUnlocksAKind_PaysThatKind(int completedLevel, PowerUpKind unlockedKind)
        {
            // The frontier after clearing N is N + 1, which is exactly the kind's gate.
            Assert.AreEqual(completedLevel + 1, PowerUpUnlockLevels.LevelFor(unlockedKind));

            IReadOnlyList<PowerUpKind> rewards = LevelCompletionRewards.For(completedLevel);

            Assert.AreEqual(1, rewards.Count);
            Assert.AreEqual(unlockedKind, rewards[0]);
        }

        [Test]
        public void TheClearThatUnlocksCoinSower_PaysARandomStripKindInstead()
        {
            Assert.AreEqual(35, PowerUpUnlockLevels.LevelFor(PowerUpKind.CoinSower));

            IReadOnlyList<PowerUpKind> rewards = LevelCompletionRewards.For(34);

            Assert.AreEqual(1, rewards.Count);
            Assert.AreNotEqual(PowerUpKind.CoinSower, rewards[0]);
            Assert.IsTrue(PowerUpUnlockLevels.IsUnlockedAt(rewards[0], 35));
        }

        [Test]
        public void AMilestoneThatUnlocksAKind_IncludesItAmongThreeDifferentKinds()
        {
            // No shipped gate lands on a milestone clear (gates are multiples of 5, so their clears are
            // 4, 9, 14, …), so this uses a table whose gate does.
            IReadOnlyList<PowerUpKind> rewards = LevelCompletionRewards.For(
                10, AllStripKinds(), kind => kind == PowerUpKind.Joker ? 11 : PowerUpUnlockLevels.ALWAYS_UNLOCKED);

            Assert.AreEqual(3, rewards.Count);
            Assert.AreEqual(PowerUpKind.Joker, rewards[0]);
            AssertDistinct(rewards);
        }

        // --- Regular vs milestone counts ---

        [Test]
        public void EveryLevel_PaysAtLeastOne_AndMilestonesPayExactlyThreeDifferentKinds()
        {
            for (int level = 1; level <= SWEEP_LAST_LEVEL; level++)
            {
                IReadOnlyList<PowerUpKind> rewards = LevelCompletionRewards.For(level);
                bool isMilestone = level % 5 == 0;

                Assert.AreEqual(isMilestone ? 3 : 1, rewards.Count, $"Level {level}");
                Assert.AreEqual(isMilestone, LevelCompletionRewards.IsMilestone(level), $"Level {level}");
                AssertDistinct(rewards);
            }
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(6)]
        [TestCase(11)]
        [TestCase(99)]
        public void ANonMilestoneLevel_NeverPaysTheBundle(int level)
        {
            Assert.IsFalse(LevelCompletionRewards.IsMilestone(level));
            Assert.AreEqual(1, LevelCompletionRewards.For(level).Count);
        }

        // --- Pool ---

        [Test]
        public void HoldAndCoinSower_AreNeverPaid()
        {
            for (int level = 1; level <= SWEEP_LAST_LEVEL; level++)
            {
                IReadOnlyList<PowerUpKind> rewards = LevelCompletionRewards.For(level);
                for (int rewardIndex = 0; rewardIndex < rewards.Count; rewardIndex++)
                {
                    Assert.AreNotEqual(PowerUpKind.Hold, rewards[rewardIndex], $"Level {level}");
                    Assert.AreNotEqual(PowerUpKind.CoinSower, rewards[rewardIndex], $"Level {level}");
                }
            }
        }

        [Test]
        public void EveryReward_IsUnlockedOnceTheLevelIsCleared()
        {
            for (int level = 1; level <= SWEEP_LAST_LEVEL; level++)
            {
                IReadOnlyList<PowerUpKind> rewards = LevelCompletionRewards.For(level);
                for (int rewardIndex = 0; rewardIndex < rewards.Count; rewardIndex++)
                {
                    Assert.IsTrue(
                        PowerUpUnlockLevels.IsUnlockedAt(rewards[rewardIndex], level + 1),
                        $"Level {level} pays {rewards[rewardIndex]}, still locked at frontier {level + 1}.");
                }
            }
        }

        [Test]
        public void EarlyLevels_OnlyDrawFromTheStarterThree()
        {
            // Levels 1–3 clear onto frontiers 2–4, before Joker's gate: only the starters are unlocked.
            for (int level = 1; level <= 3; level++)
            {
                PowerUpKind reward = LevelCompletionRewards.For(level)[0];
                Assert.IsTrue(
                    reward == PowerUpKind.Bomb || reward == PowerUpKind.RowClear || reward == PowerUpKind.ColumnClear,
                    $"Level {level} paid {reward}");
            }
        }

        // --- Determinism ---

        [Test]
        public void TheSameLevel_AlwaysPaysTheSameKinds()
        {
            for (int level = 1; level <= SWEEP_LAST_LEVEL; level++)
            {
                IReadOnlyList<PowerUpKind> first = LevelCompletionRewards.For(level);
                IReadOnlyList<PowerUpKind> second = LevelCompletionRewards.For(level);

                CollectionAssert.AreEqual(first, second, $"Level {level}");
            }
        }

        [Test]
        public void TheRandomPick_IsNotTheSameKindForEveryLevel()
        {
            // Guards against a degenerate hash: over the sweep the regular levels should spread across
            // more than one kind.
            HashSet<PowerUpKind> seen = new HashSet<PowerUpKind>();
            for (int level = 1; level <= SWEEP_LAST_LEVEL; level++)
            {
                seen.Add(LevelCompletionRewards.For(level)[0]);
            }

            Assert.Greater(seen.Count, 3);
        }

        // --- Few-unlocked edge cases ---

        [Test]
        public void AMilestoneWithOnlyTwoUnlockedKinds_PaysEachOnce_NeverADuplicate()
        {
            PowerUpKind[] roster = { PowerUpKind.Bomb, PowerUpKind.RowClear, PowerUpKind.Joker };
            IReadOnlyList<PowerUpKind> rewards = LevelCompletionRewards.For(
                5, roster, kind => kind == PowerUpKind.Joker ? 50 : PowerUpUnlockLevels.ALWAYS_UNLOCKED);

            Assert.AreEqual(2, rewards.Count);
            AssertDistinct(rewards);
            CollectionAssert.DoesNotContain(rewards, PowerUpKind.Joker);
        }

        [Test]
        public void AMilestoneWhoseOnlyKindIsTheOneItUnlocks_PaysJustThatKind()
        {
            PowerUpKind[] roster = { PowerUpKind.Joker };
            IReadOnlyList<PowerUpKind> rewards = LevelCompletionRewards.For(5, roster, kind => 6);

            Assert.AreEqual(1, rewards.Count);
            Assert.AreEqual(PowerUpKind.Joker, rewards[0]);
        }

        // --- Helpers ---

        private static PowerUpKind[] AllStripKinds()
        {
            return new[]
            {
                PowerUpKind.Bomb, PowerUpKind.RowClear, PowerUpKind.ColumnClear, PowerUpKind.Joker,
                PowerUpKind.ColorCleanser, PowerUpKind.Rotate, PowerUpKind.Reroll,
                PowerUpKind.DoubleMultiplier, PowerUpKind.GhostFit, PowerUpKind.PaintCross,
            };
        }

        private static void AssertDistinct(IReadOnlyList<PowerUpKind> rewards)
        {
            for (int outer = 0; outer < rewards.Count; outer++)
            {
                for (int inner = outer + 1; inner < rewards.Count; inner++)
                {
                    Assert.AreNotEqual(rewards[outer], rewards[inner], "A reward list repeats a kind.");
                }
            }
        }
    }
}
