namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// The <see cref="GameOverReason.NoMovesLeft"/> ending most recently announced by
    /// <see cref="GameOverMessage"/> has been taken back (issue #370): the player accepted the ad-gated
    /// rescue, the dock was replaced with a fresh set and the run is live again. Published by
    /// <c>BoardSystem</c> before it re-checks the new dock, so a subscriber that also listens for
    /// <see cref="GameOverMessage"/> sees "rescued" strictly before any second "over".
    /// <para>
    /// The signal for everything that reacted to the ending to undo itself — the countdown to resume,
    /// the end-of-run card to close, the strip to re-enable. Never published for a declined or failed
    /// rescue: the run stays ended and nothing has to be undone.
    /// </para>
    /// </summary>
    public readonly struct RunRescuedMessage
    {
    }
}
