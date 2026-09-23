namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published once per <see cref="EmptyCellBonusCountingMessage"/> by the board view, when the
    /// empty-cell count-up has finished showing (issue #424) — or immediately, if there was nothing to
    /// show — so <see cref="Systems.LevelProgressionSystem"/> can go on to end the run. Same
    /// request/completion shape as <see cref="PowerUpGrantAnimationCompletedMessage"/>: the system
    /// also guards the wait with a timeout, so a missing view can never hold the result screen hostage.
    /// </summary>
    public readonly struct EmptyCellBonusCountingCompletedMessage
    {
    }
}
