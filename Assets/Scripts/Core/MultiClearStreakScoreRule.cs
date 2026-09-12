using System;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Rewards chaining 2+-line clears back to back. Reuses <see cref="ScoreRules.StreakBonus"/>'s exact
    /// +0.5x-per-step / +3.0x-cap formula rather than declaring a new one: the escalation shape is identical
    /// to the existing combo streak, only the run-scoped counter it reads differs (consecutive multi-line
    /// clears instead of consecutive clearing placements). Duplicating the math under a new name would mean
    /// two places to keep in sync.
    /// <para>
    /// Only a placement that itself clears 2+ lines earns this bonus. A single-line clear (which resets the
    /// streak) or a non-clearing placement scores zero here, even though it may change the streak's future
    /// value. The bonus is a multiplier on the same 10 x lines base as <see cref="ScoreRules.ClearScore"/>,
    /// so it stacks additively next to the combo and monochrome bonuses.
    /// </para>
    /// </summary>
    public sealed class MultiClearStreakScoreRule : IScoreRule
    {
        public int ComputeBonus(ScorePlacementContext context)
        {
            if (context.LinesCleared < 2)
            {
                return 0;
            }

            double bonusMultiplier = ScoreRules.StreakBonus(context.MultiClearStreakBeforePlacement);
            double rawBonus = ScoreRules.POINTS_PER_LINE * context.LinesCleared * bonusMultiplier;
            return (int)Math.Round(rawBonus, MidpointRounding.AwayFromZero);
        }
    }
}
