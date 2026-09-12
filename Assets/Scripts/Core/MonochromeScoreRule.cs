using System;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Rewards clearing a line that was entirely one colour — see
    /// <see cref="ScoreRules.MonochromeMultiplierBonus"/>. The bonus is a multiplier stacked onto the same
    /// 10 x lines base as <see cref="ScoreRules.ClearScore"/>, so it composes additively with the combo
    /// multiplier and streak bonus. Applies only when lines actually cleared (mirrors
    /// <see cref="LineClearScoreRule"/>'s zero-lines guard).
    /// </summary>
    public sealed class MonochromeScoreRule : IScoreRule
    {
        public int ComputeBonus(ScorePlacementContext context)
        {
            if (context.LinesCleared <= 0 || context.MonochromeLineCount <= 0)
            {
                return 0;
            }

            double bonusMultiplier = ScoreRules.MonochromeMultiplierBonus(context.MonochromeLineCount);
            double rawBonus = ScoreRules.POINTS_PER_LINE * context.LinesCleared * bonusMultiplier;
            return (int)Math.Round(rawBonus, MidpointRounding.AwayFromZero);
        }
    }
}
