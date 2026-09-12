namespace MustyBlockBlast.Core
{
    /// <summary>Rewards a placement whose line clears leave the board completely empty — a rare "perfect
    /// clear" / board-wipe, worth a large flat bonus: <see cref="ScoreRules.BoardWipeBonus"/>.</summary>
    public sealed class BoardWipeScoreRule : IBonusScoreRule
    {
        public int ComputeBonus(ScorePlacementContext context)
        {
            return ScoreRules.BoardWipeBonus(context.LinesCleared, context.BoardEmptyAfterPlacement);
        }
    }
}
