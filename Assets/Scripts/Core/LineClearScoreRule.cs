namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Combo plus streak points for cleared lines: <see cref="ScoreRules.ClearScore"/>, which already
    /// returns zero when nothing cleared.
    /// </summary>
    public sealed class LineClearScoreRule : IScoreRule
    {
        public int ComputeBonus(ScorePlacementContext context)
        {
            if (context.LinesCleared <= 0)
            {
                return 0;
            }

            return ScoreRules.ClearScore(context.LinesCleared, context.StreakBeforePlacement);
        }
    }
}
