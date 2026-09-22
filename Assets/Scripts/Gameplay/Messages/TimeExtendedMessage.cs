namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// The Timed-mode match clock was just extended by a placement's line clears (issue #319).
    /// Published by <c>TimerRunSystem</c> — the only writer of <c>TimerModel.RemainingSeconds</c> —
    /// strictly after the seconds have been added, so a subscriber reading the model sees the new
    /// total. Never published for a placement that cleared nothing, outside Timed mode, or while no
    /// countdown is running: the HUD's "+Ns" acknowledgement must only ever appear when the clock
    /// genuinely moved.
    /// </summary>
    public readonly struct TimeExtendedMessage
    {
        public TimeExtendedMessage(int linesCleared, float secondsAdded)
        {
            LinesCleared = linesCleared;
            SecondsAdded = secondsAdded;
        }

        /// <summary>How many rows plus columns the placement cleared — the multiplier behind
        /// <see cref="SecondsAdded"/>.</summary>
        public int LinesCleared { get; }

        /// <summary>Seconds just added to the clock, already multiplied out: the HUD formats this
        /// number and nothing else.</summary>
        public float SecondsAdded { get; }
    }
}
