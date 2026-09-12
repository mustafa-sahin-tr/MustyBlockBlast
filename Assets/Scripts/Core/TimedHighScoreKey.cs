using System;
using System.Globalization;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Builds the persistence key for a timed-mode high score. Timed difficulty depends entirely on
    /// the round length, so every length keeps its own best — one key per duration.
    /// <para>
    /// The duration is rounded to whole seconds on purpose: the configured lengths are always whole
    /// seconds, and rounding keeps <c>15f</c> and <c>15.0000001f</c> on the same key instead of
    /// silently splitting one leaderboard in two after a float round-trip.
    /// </para>
    /// </summary>
    public static class TimedHighScoreKey
    {
        private const string KEY_PREFIX = "Score.HighScore.Timed.";

        /// <summary>Stable storage key for the given round length, e.g. <c>Score.HighScore.Timed.15</c>.</summary>
        public static string For(float durationSeconds)
        {
            int wholeSeconds = (int)Math.Round(durationSeconds, MidpointRounding.AwayFromZero);
            return KEY_PREFIX + wholeSeconds.ToString(CultureInfo.InvariantCulture);
        }
    }
}
