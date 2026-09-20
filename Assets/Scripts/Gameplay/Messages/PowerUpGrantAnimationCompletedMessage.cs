namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published once per <see cref="PowerUpGrantedMessage"/> instance, whether or not that grant's
    /// flight icon actually flew (issue #353) — a grant with no resolvable inventory slot or no
    /// configured icon still publishes this immediately, so nothing waiting on it can hang forever.
    /// <see cref="InfoPopupSystem"/> is the sole subscriber: it holds its first-time explainer card
    /// open until the icon for the same grant has visibly landed, so the card never covers it mid-flight.
    /// </summary>
    public readonly struct PowerUpGrantAnimationCompletedMessage
    {
        public PowerUpGrantAnimationCompletedMessage(PowerUpKind kind)
        {
            Kind = kind;
        }

        public PowerUpKind Kind { get; }
    }
}
