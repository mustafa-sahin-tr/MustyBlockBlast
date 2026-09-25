namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// A Path run was asked to start with zero lives and refused (issue #478). Published by
    /// <c>LivesSystem.TryPassStartGate</c> — the one gate every Path start goes through, whether from the
    /// level-start card, "Next level" or "Try again" — so the refusal is announced once, by the one
    /// place that decides it, rather than by each caller.
    /// <para>
    /// The out-of-lives sheet opens on it. Nothing else changes: no board was dealt and no run state
    /// moved, so there is nothing for any other subscriber to undo.
    /// </para>
    /// </summary>
    public readonly struct OutOfLivesMessage
    {
    }
}
