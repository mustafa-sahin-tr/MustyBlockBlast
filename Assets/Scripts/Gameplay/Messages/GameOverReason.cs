namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>Why a run ended. Lets the end-of-run card read differently per game mode.</summary>
    public enum GameOverReason
    {
        /// <summary>No remaining tray piece fits anywhere on the board.</summary>
        NoMovesLeft,

        /// <summary>The timed-mode clock reached zero.</summary>
        TimeUp
    }
}
