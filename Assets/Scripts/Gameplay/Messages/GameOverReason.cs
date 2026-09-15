namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>Why a run ended. Lets the end-of-run card read differently per game mode.</summary>
    public enum GameOverReason
    {
        /// <summary>No remaining tray piece fits anywhere on the board.</summary>
        NoMovesLeft,

        /// <summary>The timed-mode clock reached zero.</summary>
        TimeUp,

        /// <summary>
        /// The Path-mode run met its level's objective. The only reason that is a success rather than
        /// a failure — it shares the end-of-run card with the other two deliberately, since "the run
        /// is over, here is what it scored" is one screen whichever way it ended.
        /// </summary>
        LevelCompleted,
    }
}
