namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published by <c>ModeSelectSystem</c> the moment a mode is picked on the mode-select screen,
    /// immediately before it starts loading the gameplay scene. The one signal that "the hand-over
    /// has begun", so the loading curtain can drop before the scene load stalls the main thread.
    /// Published at most once per mode-select scene: a second tap during the load is ignored upstream.
    /// </summary>
    public readonly struct GameModeChosenMessage
    {
        public GameModeChosenMessage(GameMode mode)
        {
            Mode = mode;
        }

        /// <summary>The mode about to be played, so the curtain can wear that mode's glyph.</summary>
        public GameMode Mode { get; }
    }
}
