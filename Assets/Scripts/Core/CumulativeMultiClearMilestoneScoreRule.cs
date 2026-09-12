namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Rewards reaching a cumulative multi-line-clear milestone (5th, 10th, 15th, ... occurrence this run),
    /// independent of whether the occurrences were consecutive — see <see cref="MultiClearStreakScoreRule"/>
    /// for the separate consecutive-streak bonus that stacks alongside this one. A flat bonus (see
    /// <see cref="ScoreRules.MultiClearMilestoneBonus"/>), not a multiplier on the clear-score base.
    /// <para>
    /// One placement counts as at most one occurrence regardless of how many lines it cleared, matching the
    /// per-clear-event granularity <see cref="ScoreRules.ComboMultiplier"/> already uses.
    /// </para>
    /// </summary>
    public sealed class CumulativeMultiClearMilestoneScoreRule : IScoreRule
    {
        public int ComputeBonus(ScorePlacementContext context)
        {
            if (context.LinesCleared < 2)
            {
                return 0;
            }

            // This placement itself IS an occurrence, so the milestone fires on the placement that reaches
            // it rather than one placement later.
            int occurrenceCount = context.CumulativeMultiClearCountBeforePlacement + 1;
            return ScoreRules.MultiClearMilestoneBonus(occurrenceCount);
        }
    }
}
