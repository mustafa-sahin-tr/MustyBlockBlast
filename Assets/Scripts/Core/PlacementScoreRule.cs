namespace MustyBlockBlast.Core
{
    /// <summary>Base points for dropping a piece: <see cref="ScoreRules.PlacementScore"/>.</summary>
    public sealed class PlacementScoreRule : IScoreRule
    {
        public int ComputeBonus(ScorePlacementContext context)
        {
            return ScoreRules.PlacementScore(context.CellCount);
        }
    }
}
