namespace MustyBlockBlast.Gameplay.Messages
{
    public readonly struct ScoreChangedMessage
    {
        public ScoreChangedMessage(int total, int gained, int streak)
        {
            Total = total;
            Gained = gained;
            Streak = streak;
        }

        public int Total { get; }

        public int Gained { get; }

        public int Streak { get; }
    }
}
