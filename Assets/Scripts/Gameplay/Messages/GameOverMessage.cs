namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// The run is over. <see cref="Reason"/> says why, so Views can word the card per mode.
    /// <para>
    /// May be published more than once per run (issue #370): a <see cref="GameOverReason.NoMovesLeft"/>
    /// ending with <see cref="IsRescueAvailable"/> set can be undone by
    /// <c>BoardSystem.TryApplyNoMovesRescueAsync</c>, after which the run continues and may end again —
    /// with another message. Subscribers that bank something per run must therefore treat this as "the
    /// run is over as of now", not as a once-only event; <see cref="RunRescuedMessage"/> is the
    /// counterpart that says the previous ending was taken back.
    /// </para>
    /// </summary>
    public readonly struct GameOverMessage
    {
        public GameOverMessage(GameOverReason reason)
            : this(reason, isRescueAvailable: false)
        {
        }

        public GameOverMessage(GameOverReason reason, bool isRescueAvailable)
        {
            Reason = reason;
            IsRescueAvailable = isRescueAvailable;
        }

        public GameOverReason Reason { get; }

        /// <summary>
        /// True when the player may take back this ending by watching a rewarded ad, through
        /// <c>BoardSystem.TryApplyNoMovesRescueAsync</c> (issue #370). Only ever set on a
        /// <see cref="GameOverReason.NoMovesLeft"/> ending, in every mode, and only once the Demolition
        /// Hammer life-line has already failed to save the run — <see cref="GameOverReason.TimeUp"/>,
        /// <see cref="GameOverReason.LevelCompleted"/> and <see cref="GameOverReason.ObjectiveMissed"/>
        /// never carry it. Unlimited: a rescued run that locks up again is offered it again.
        /// </summary>
        public bool IsRescueAvailable { get; }
    }
}
