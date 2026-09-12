namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published alongside <see cref="ScoreChangedMessage"/> only when at least one
    /// <see cref="MustyBlockBlast.Core.IBonusScoreRule"/> contributed points to this placement — the flat
    /// combined bonus amount, not attributed to any specific rule (#61).
    /// </summary>
    public readonly struct BonusScoredMessage
    {
        public BonusScoredMessage(int bonusAmount)
        {
            BonusAmount = bonusAmount;
        }

        public int BonusAmount { get; }
    }
}
