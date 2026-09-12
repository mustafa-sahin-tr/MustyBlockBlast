using System;

namespace MustyBlockBlast.Core
{
    /// <summary>Pure scoring formulas (see docs/game-design.md, "Scoring"). Streak state itself is
    /// owned by the caller (Gameplay) — these are stateless functions of that state.</summary>
    public static class ScoreRules
    {
        private const int POINTS_PER_PLACED_CELL = 1;
        /// <summary>Internal so sibling rules in this assembly (e.g. MonochromeScoreRule) reuse the constant
        /// instead of duplicating a magic 10; still not part of the public API.</summary>
        internal const int POINTS_PER_LINE = 10;

        private const double STREAK_BONUS_PER_STEP = 0.5;
        private const double MAX_STREAK_BONUS = 3.0;
        private const double MONOCHROME_BONUS_PER_LINE = 0.5;

        private const int MULTI_CLEAR_MILESTONE_STEP = 5;
        private const int MULTI_CLEAR_MILESTONE_REWARD_PER_STEP = 10;

        /// <summary>+1 point per cell of the piece just placed.</summary>
        public static int PlacementScore(int cellCount) => cellCount * POINTS_PER_PLACED_CELL;

        /// <summary>1, 3, 6, 10 for 1, 2, 3, 4+ simultaneous lines; 0 when nothing cleared.</summary>
        public static double ComboMultiplier(int lines)
        {
            if (lines <= 0)
            {
                return 0;
            }

            switch (lines)
            {
                case 1:
                    return 1;
                case 2:
                    return 3;
                case 3:
                    return 6;
                default:
                    return 10;
            }
        }

        /// <summary>+0.5x per consecutive clearing placement, capped at +3x.</summary>
        public static double StreakBonus(int streak)
        {
            if (streak <= 0)
            {
                return 0;
            }

            return Math.Min(streak * STREAK_BONUS_PER_STEP, MAX_STREAK_BONUS);
        }

        /// <summary>+0.5x per cleared line that was entirely one colour, stacking additively into the clear
        /// multiplier alongside the combo multiplier and streak bonus.</summary>
        public static double MonochromeMultiplierBonus(int monochromeLineCount)
        {
            if (monochromeLineCount <= 0)
            {
                return 0;
            }

            return monochromeLineCount * MONOCHROME_BONUS_PER_LINE;
        }

        /// <summary>Flat bonus when <paramref name="occurrenceCount"/> (the cumulative multi-line-clear count
        /// AFTER this placement) lands exactly on a multiple of 5 — the 5th, 10th, 15th, ... occurrence this
        /// run. Reward scales with the milestone reached (occurrenceCount x 10). Zero otherwise.</summary>
        public static int MultiClearMilestoneBonus(int occurrenceCount)
        {
            if (occurrenceCount <= 0 || occurrenceCount % MULTI_CLEAR_MILESTONE_STEP != 0)
            {
                return 0;
            }

            return occurrenceCount * MULTI_CLEAR_MILESTONE_REWARD_PER_STEP;
        }

        /// <summary>10 x lines x (comboMultiplier(lines) + streakBonus(streak)). Zero when no lines cleared.</summary>
        public static int ClearScore(int lines, int streak)
        {
            if (lines <= 0)
            {
                return 0;
            }

            double multiplier = ComboMultiplier(lines) + StreakBonus(streak);
            double rawScore = POINTS_PER_LINE * lines * multiplier;
            return (int)Math.Round(rawScore, MidpointRounding.AwayFromZero);
        }
    }
}
