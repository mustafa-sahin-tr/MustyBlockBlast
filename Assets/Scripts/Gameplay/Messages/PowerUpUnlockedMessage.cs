namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published the moment a <see cref="PowerUpKind"/>'s level gate (see
    /// <see cref="PowerUpUnlockLevels"/>) is newly crossed by the player's progression frontier —
    /// never retroactively for a kind that was already unlocked when the listener started watching.
    /// <see cref="MustyBlockBlast.Gameplay.Systems.TutorialSystem"/> is the one subscriber today: it
    /// decides whether the player has already seen this kind's coach-mark and, if not, queues one.
    /// </summary>
    public readonly struct PowerUpUnlockedMessage
    {
        public PowerUpUnlockedMessage(PowerUpKind kind)
        {
            Kind = kind;
        }

        public PowerUpKind Kind { get; }
    }
}
