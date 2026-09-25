namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Asks the grant/special-cell fly-in to report back once every flight already queued — and every
    /// one the same placement is still about to queue — has landed. Published by
    /// <see cref="Systems.LevelProgressionSystem"/> when a Path level is cleared, so special cells won on
    /// the final move settle onto the board before the empty-cell count and the result screen. Answered
    /// by <see cref="PendingFlightsDrainedMessage"/>; the system guards the wait with a timeout.
    /// </summary>
    public readonly struct PendingFlightsDrainMessage
    {
    }
}
