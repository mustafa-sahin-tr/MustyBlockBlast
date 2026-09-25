namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published once per <see cref="PendingFlightsDrainMessage"/> when the fly-in queue has emptied — or
    /// at once, if nothing was flying — so a cleared level can go on to count its empty cells.
    /// </summary>
    public readonly struct PendingFlightsDrainedMessage
    {
    }
}
