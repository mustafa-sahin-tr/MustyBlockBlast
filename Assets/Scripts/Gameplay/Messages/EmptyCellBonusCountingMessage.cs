namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published by <see cref="Systems.LevelProgressionSystem"/> the moment a Path-mode level is
    /// cleared, before the run is declared over (issue #424): the board should count its remaining
    /// empty cells out loud — 1, 2, 3… written over the cells themselves — because each of them is
    /// worth one bonus point. The board view answers with
    /// <see cref="EmptyCellBonusCountingCompletedMessage"/> once the count has been shown, which is
    /// what the system waits for before it ends the run and lets the result screen appear.
    /// </summary>
    public readonly struct EmptyCellBonusCountingMessage
    {
        public EmptyCellBonusCountingMessage(int emptyCellCount)
        {
            EmptyCellCount = emptyCellCount;
        }

        /// <summary>How many empty cells the system counted — the bonus being paid, and the number the
        /// board is expected to count up to.</summary>
        public int EmptyCellCount { get; }
    }
}
