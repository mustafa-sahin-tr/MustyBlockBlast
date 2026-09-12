namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Marks an <see cref="IScoreRule"/> as a bonus (not base placement/line-clear scoring), so the
    /// scoring system can report a combined bonus total for player-facing feedback without attributing
    /// it to any specific rule (#61). Purely structural — it adds no members and does not change what
    /// any rule computes.
    /// </summary>
    public interface IBonusScoreRule : IScoreRule
    {
    }
}
