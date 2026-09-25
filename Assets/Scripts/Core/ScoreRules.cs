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

        private const int BOARD_WIPE_BONUS = 200;

        /// <summary>What one scoring event is multiplied by when it destroyed a
        /// <see cref="SpecialCellKind.ScoreGem"/>.</summary>
        public const int SCORE_GEM_FACTOR = 3;

        /// <summary>Bonus points each cell of a puzzle-link group pays when the whole group is taken out in
        /// one resolution (issue #483) — a group of two is worth 20, of three 30.</summary>
        public const int PUZZLE_LINK_BONUS_PER_CELL = 10;

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

        /// <summary>+0.5x per consecutive clearing placement, capped at +3x. Used only by
        /// <see cref="MultiClearStreakScoreRule"/>'s own chain (consecutive 2+-line clears) — a much
        /// rarer event than <see cref="ComboStreakBonus"/>'s ordinary any-clear streak, which is why
        /// the two no longer share a cap: the sub-issue that split them decided this one still should.
        /// </summary>
        public static double StreakBonus(int streak)
        {
            if (streak <= 0)
            {
                return 0;
            }

            return Math.Min(streak * STREAK_BONUS_PER_STEP, MAX_STREAK_BONUS);
        }

        /// <summary>
        /// +1x per consecutive clearing placement beyond the first, uncapped: the multiplier a player
        /// sees on the streak pill is exactly <c>1 + ComboStreakBonus(streak)</c>, i.e. the streak count
        /// itself. Deliberately has no ceiling — a chain that long is its own limiter; the board runs
        /// out of room to keep completing lines back to back long before any cap would matter — so
        /// unlike <see cref="StreakBonus"/> this never plateaus.
        /// </summary>
        public static double ComboStreakBonus(int streak) => streak <= 1 ? 0 : streak - 1;

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

        /// <summary>Large flat bonus when a placement's line clears leave the board completely empty
        /// ("perfect clear"). Zero unless lines were actually cleared this placement AND the board ended up
        /// empty.</summary>
        public static int BoardWipeBonus(int linesCleared, bool boardEmptyAfterPlacement)
        {
            if (linesCleared <= 0 || !boardEmptyAfterPlacement)
            {
                return 0;
            }

            return BOARD_WIPE_BONUS;
        }

        /// <summary>
        /// <paramref name="points"/> as the event should actually be credited them once the
        /// <see cref="SpecialCellKind.ScoreGem"/>s it destroyed are taken into account: tripled when it
        /// destroyed at least one, untouched otherwise.
        /// <para>
        /// Applied to a finished event total rather than to any individual rule, so the whole additive
        /// stack — placement, clears, streak and milestone bonuses — is computed exactly as it always
        /// is and only its output is tripled. It composes with, rather than replaces, the 2x frenzy
        /// window (<c>DoubleMultiplierModel.Multiply</c>): a gem destroyed inside a frenzy is worth 6x,
        /// because each multiplier is applied to the running total in turn.
        /// </para>
        /// <para>
        /// Deliberately has no floor, exactly as the frenzy's doubling has none: zero points tripled is
        /// still zero, so an event that scored nothing scores nothing however many gems went with it.
        /// </para>
        /// <para>
        /// Flat, not compounding: two gems in one event triple it once rather than nine-folding it.
        /// The reward is for the event having reached a gem at all, and a compounding factor would make
        /// a single lucky sweep worth more than the rest of a run put together.
        /// </para>
        /// </summary>
        public static int ScoreGemMultiplied(int points, int destroyedScoreGemCount)
            => destroyedScoreGemCount > 0 ? points * SCORE_GEM_FACTOR : points;

        /// <summary>10 x lines x (comboMultiplier(lines) + comboStreakBonus(streak)). Zero when no lines
        /// cleared.</summary>
        public static int ClearScore(int lines, int streak)
        {
            if (lines <= 0)
            {
                return 0;
            }

            double multiplier = ComboMultiplier(lines) + ComboStreakBonus(streak);
            double rawScore = POINTS_PER_LINE * lines * multiplier;
            return (int)Math.Round(rawScore, MidpointRounding.AwayFromZero);
        }
    }
}
