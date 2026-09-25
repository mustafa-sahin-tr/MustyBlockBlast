namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// One or more whole puzzle-link groups (issue #483) were taken out in the resolution that just ended
    /// — every member hit in that one resolution — and removed: <see cref="CellCount"/> link cells in
    /// all. Published by whichever System resolved the destruction, after it has announced the emptied
    /// cells; <see cref="Systems.PuzzleLinkScoreSystem"/> pays the bonus. Published only when something
    /// was removed.
    /// </summary>
    public readonly struct PuzzleLinksClearedMessage
    {
        public PuzzleLinksClearedMessage(int cellCount)
        {
            CellCount = cellCount;
        }

        /// <summary>Link cells removed. Always greater than zero.</summary>
        public int CellCount { get; }
    }
}
