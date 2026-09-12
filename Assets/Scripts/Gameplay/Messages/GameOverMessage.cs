namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>The run is over. <see cref="Reason"/> says why, so Views can word the card per mode.</summary>
    public readonly struct GameOverMessage
    {
        public GameOverMessage(GameOverReason reason)
        {
            Reason = reason;
        }

        public GameOverReason Reason { get; }
    }
}
