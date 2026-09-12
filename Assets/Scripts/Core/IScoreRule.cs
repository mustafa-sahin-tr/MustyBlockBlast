namespace MustyBlockBlast.Core
{
    /// <summary>
    /// One additive contribution to the score gained by a placement. Implementations are stateless
    /// and are summed by the scoring system, so a new bonus is a new class plus one DI registration.
    /// </summary>
    public interface IScoreRule
    {
        /// <summary>Points this rule contributes for the given placement; zero when it does not apply.</summary>
        int ComputeBonus(ScorePlacementContext context);
    }
}
