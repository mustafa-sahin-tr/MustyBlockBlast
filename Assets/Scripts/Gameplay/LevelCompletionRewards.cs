using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// The rule that decides which power-ups completing a level pays out (issue #462). Every level
    /// rewards; nothing is authored per level. Pure and deterministic — a function of the level number
    /// and the unlock table alone — so the Level Path can preview the exact reward before the level is
    /// played and the preview can never disagree with the payout.
    /// <para>
    /// <b>The rule.</b> A regular level pays <see cref="REGULAR_REWARD_COUNT"/> kind; every
    /// <see cref="MILESTONE_INTERVAL"/>th level pays <see cref="MILESTONE_REWARD_COUNT"/> different
    /// kinds. If completing the level unlocks a kind, that kind is always one of the rewards; every
    /// other slot is filled with a pseudo-random, distinct kind among those unlocked once the level is
    /// cleared, seeded from the level number.
    /// </para>
    /// <para>
    /// <b>Which completion unlocks a kind.</b> A kind's gate is read against the progression frontier
    /// (<see cref="PowerUpUnlockLevels.IsUnlockedAt"/>), and the frontier becomes <c>N + 1</c> the moment
    /// level <c>N</c> is first cleared. So the completion that makes a kind gated at level <c>G</c>
    /// available is the completion of level <c>G − 1</c> — e.g. clearing level 4 unlocks Joker (gate 5).
    /// The unlocked pool for level <c>N</c> is likewise "every kind whose gate is at most <c>N + 1</c>":
    /// the reward is paid only on a first clear, when that is exactly what the frontier becomes.
    /// </para>
    /// <para>
    /// <b>Rewardable kinds</b> are the power-up strip's kinds only. <see cref="PowerUpKind.Hold"/> and
    /// <see cref="PowerUpKind.CoinSower"/> are earned by rewarded ad and never sit in the strip, so they
    /// are neither drawn at random nor guaranteed — CoinSower's own unlock (gate 35) therefore pays a
    /// random strip kind instead.
    /// </para>
    /// <para>
    /// When fewer rewardable kinds are unlocked than a level's reward count, the level pays each of
    /// them once rather than repeating one: the bundle is "different power-ups" by definition, and a
    /// duplicate would quietly pass off one kind as two. Under the shipped table this never happens —
    /// the starter three are unlocked from the start, so every milestone has at least three to draw.
    /// </para>
    /// </summary>
    public static class LevelCompletionRewards
    {
        /// <summary>Every this-many levels (5, 10, 15, …) is a milestone that pays a bundle.</summary>
        public const int MILESTONE_INTERVAL = 5;

        /// <summary>How many different kinds a milestone level pays.</summary>
        public const int MILESTONE_REWARD_COUNT = 3;

        /// <summary>How many kinds every other level pays.</summary>
        public const int REGULAR_REWARD_COUNT = 1;

        /// <summary>Spreads consecutive level numbers apart before hashing, so neighbouring levels do
        /// not draw neighbouring hashes. Any odd constant works; changing it reshuffles every level's
        /// random reward.</summary>
        private const uint SEED_MULTIPLIER = 2654435761u;

        /// <summary>First draw index a rule bonus (issue #464) hashes with — well past the handful
        /// <see cref="For(int)"/> can use for the level's own reward.</summary>
        private const int BONUS_DRAW_INDEX_OFFSET = 64;

        /// <summary>
        /// The kinds a level can pay: the power-up strip's kinds, in strip order. The order matters
        /// only for determinism — it is the order the pool is drawn from.
        /// </summary>
        private static readonly PowerUpKind[] RewardableKinds =
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
            PowerUpKind.PaintCross,
        };

        private static readonly Func<PowerUpKind, int> ShippedUnlockTable = PowerUpUnlockLevels.LevelFor;

        /// <summary>Whether <paramref name="levelNumber"/> is a milestone that pays
        /// <see cref="MILESTONE_REWARD_COUNT"/> kinds.</summary>
        public static bool IsMilestone(int levelNumber)
            => levelNumber > 0 && levelNumber % MILESTONE_INTERVAL == 0;

        /// <summary>
        /// The power-ups a first clear of <paramref name="completedLevelNumber"/> pays, in grant order:
        /// the kind it unlocks (if any) first, then the random picks. Same input, same list, always.
        /// </summary>
        public static IReadOnlyList<PowerUpKind> For(int completedLevelNumber)
            => For(completedLevelNumber, RewardableKinds, ShippedUnlockTable);

        /// <summary>
        /// <see cref="For(int)"/> against an arbitrary roster and unlock table — the seam the tests use
        /// to exercise rosters the shipped table never produces (e.g. fewer unlocked kinds than a
        /// milestone pays).
        /// </summary>
        internal static IReadOnlyList<PowerUpKind> For(
            int completedLevelNumber, IReadOnlyList<PowerUpKind> rewardableKinds, Func<PowerUpKind, int> unlockLevelFor)
        {
            int rewardCount = IsMilestone(completedLevelNumber) ? MILESTONE_REWARD_COUNT : REGULAR_REWARD_COUNT;
            int frontierAfterClear = completedLevelNumber + 1;

            List<PowerUpKind> rewards = new List<PowerUpKind>(rewardCount);
            List<PowerUpKind> pool = new List<PowerUpKind>(rewardableKinds.Count);

            for (int kindIndex = 0; kindIndex < rewardableKinds.Count; kindIndex++)
            {
                PowerUpKind kind = rewardableKinds[kindIndex];
                int unlockLevel = unlockLevelFor(kind);

                // Unlocked by this very clear: guaranteed, ahead of any random pick. An always-unlocked
                // kind (gate 0) can never match, since frontierAfterClear is at least 2.
                if (unlockLevel == frontierAfterClear && rewards.Count < rewardCount)
                {
                    rewards.Add(kind);
                    continue;
                }

                if (unlockLevel <= frontierAfterClear)
                {
                    pool.Add(kind);
                }
            }

            int drawIndex = 0;
            while (rewards.Count < rewardCount && pool.Count > 0)
            {
                int pick = (int)(Hash(completedLevelNumber, drawIndex) % (uint)pool.Count);
                rewards.Add(pool[pick]);
                pool.RemoveAt(pick);
                drawIndex++;
            }

            return rewards;
        }

        /// <summary>
        /// One extra kind for a rule-based bonus paid on the first clear of
        /// <paramref name="completedLevelNumber"/> (issue #464). Drawn from the same pool as the
        /// level's own reward — the strip kinds unlocked once the level is cleared — but preferring a
        /// kind <see cref="For(int)"/> did not already pay, so the bonus reads as something extra rather
        /// than a second copy. Falls back to the whole unlocked pool when the level paid every kind
        /// there is. Deterministic in (level, <paramref name="drawIndex"/>), like everything else here.
        /// </summary>
        /// <param name="drawIndex">Distinguishes several bonus kinds drawn for the same level (a rule
        /// paying more than one, or two rules paying on the same clear); 0 for the first.</param>
        public static PowerUpKind DrawBonusKind(int completedLevelNumber, int drawIndex)
            => DrawBonusKind(completedLevelNumber, drawIndex, RewardableKinds, ShippedUnlockTable);

        /// <summary><see cref="DrawBonusKind(int, int)"/> against an arbitrary roster and unlock table —
        /// the test seam, as for <see cref="For(int, IReadOnlyList{PowerUpKind}, Func{PowerUpKind, int})"/>.</summary>
        internal static PowerUpKind DrawBonusKind(
            int completedLevelNumber, int drawIndex,
            IReadOnlyList<PowerUpKind> rewardableKinds, Func<PowerUpKind, int> unlockLevelFor)
        {
            IReadOnlyList<PowerUpKind> levelRewards = For(completedLevelNumber, rewardableKinds, unlockLevelFor);
            int frontierAfterClear = completedLevelNumber + 1;

            List<PowerUpKind> unlocked = new List<PowerUpKind>(rewardableKinds.Count);
            List<PowerUpKind> notYetPaid = new List<PowerUpKind>(rewardableKinds.Count);
            for (int kindIndex = 0; kindIndex < rewardableKinds.Count; kindIndex++)
            {
                PowerUpKind kind = rewardableKinds[kindIndex];
                if (unlockLevelFor(kind) > frontierAfterClear)
                {
                    continue;
                }

                unlocked.Add(kind);
                if (!Contains(levelRewards, kind))
                {
                    notYetPaid.Add(kind);
                }
            }

            List<PowerUpKind> pool = notYetPaid.Count > 0 ? notYetPaid : unlocked;
            if (pool.Count == 0)
            {
                // Unreachable under the shipped table (the starter kinds are always unlocked); a
                // degenerate test roster still gets a defined answer rather than a crash.
                return rewardableKinds.Count > 0 ? rewardableKinds[0] : PowerUpKind.Bomb;
            }

            // Offset past every index For() could have used, so the bonus is not simply correlated with
            // the level's own draws.
            uint hash = Hash(completedLevelNumber, BONUS_DRAW_INDEX_OFFSET + drawIndex);
            return pool[(int)(hash % (uint)pool.Count)];
        }

        private static bool Contains(IReadOnlyList<PowerUpKind> kinds, PowerUpKind kind)
        {
            for (int kindIndex = 0; kindIndex < kinds.Count; kindIndex++)
            {
                if (kinds[kindIndex] == kind)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A stable integer hash of (level, draw). Hand-rolled rather than <see cref="System.Random"/>
        /// so the sequence is guaranteed identical on every runtime and platform the game ships on —
        /// a preview that could change between Mono and IL2CPP would not be a promise.
        /// </summary>
        private static uint Hash(int levelNumber, int drawIndex)
        {
            uint value = unchecked(((uint)levelNumber * SEED_MULTIPLIER) + (uint)drawIndex * 0x9E3779B9u);
            value ^= value >> 16;
            value = unchecked(value * 0x7FEB352Du);
            value ^= value >> 15;
            value = unchecked(value * 0x846CA68Bu);
            value ^= value >> 16;
            return value;
        }
    }
}
