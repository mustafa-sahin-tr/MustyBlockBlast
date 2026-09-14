namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published when clearing a level unlocked the next one and the player moved onto it. Not
    /// published on load, and not published on the final level (there is nothing to advance to), so
    /// every occurrence is a real forward step.
    /// </summary>
    public readonly struct LevelAdvancedMessage
    {
        public LevelAdvancedMessage(int currentLevelNumber)
        {
            CurrentLevelNumber = currentLevelNumber;
        }

        /// <summary>The level the player is now on — the one just unlocked.</summary>
        public int CurrentLevelNumber { get; }
    }
}
