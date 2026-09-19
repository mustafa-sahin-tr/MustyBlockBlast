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

        /// <summary>
        /// A Path-mode run failed a level requirement the moment it happened, rather than by running out
        /// of moves or time — today, exclusively a <see cref="MustyBlockBlast.Core.SpecialCellKind.Timer"/>
        /// cell's countdown reaching 0 while still uncleared (issue #307 AC4). Unconditional: any single
        /// expiry ends the run, whether or not the level's objective could still mathematically be met
        /// some other way. Never used outside Path mode — an expired timer cell in Endless/Timed simply
        /// loses its own objective credit and the run continues.
        /// </summary>
        ObjectiveMissed,
    }
}
