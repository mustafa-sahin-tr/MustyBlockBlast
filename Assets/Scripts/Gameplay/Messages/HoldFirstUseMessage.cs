namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published on every successful <c>PowerUpSystem.TryApplyHold</c> — not only the first. The
    /// publisher stays dumb on purpose: deciding whether this is the player's <em>first</em> use, and
    /// whether that earns a coach-mark, is <see cref="MustyBlockBlast.Gameplay.Systems.TutorialSystem"/>'s
    /// job alone, via its own seen-flag.
    /// </summary>
    public readonly struct HoldFirstUseMessage
    {
    }
}
