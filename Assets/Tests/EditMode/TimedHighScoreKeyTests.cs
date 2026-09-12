using MustyBlockBlast.Core;
using NUnit.Framework;

namespace MustyBlockBlast.Tests.EditMode
{
    public class TimedHighScoreKeyTests
    {
        [TestCase(5f, "Score.HighScore.Timed.5")]
        [TestCase(10f, "Score.HighScore.Timed.10")]
        [TestCase(15f, "Score.HighScore.Timed.15")]
        [TestCase(20f, "Score.HighScore.Timed.20")]
        [TestCase(25f, "Score.HighScore.Timed.25")]
        public void For_ConfiguredDurations_UseWholeSecondsWithNoDecimalPoint(float seconds, string expected)
        {
            Assert.AreEqual(expected, TimedHighScoreKey.For(seconds));
        }

        [Test]
        public void For_EveryConfiguredDuration_ProducesADistinctKey()
        {
            string[] keys =
            {
                TimedHighScoreKey.For(5f),
                TimedHighScoreKey.For(10f),
                TimedHighScoreKey.For(15f),
                TimedHighScoreKey.For(20f),
                TimedHighScoreKey.For(25f),
            };

            CollectionAssert.AllItemsAreUnique(keys);
        }

        [Test]
        public void For_FloatRoundTripNoise_StillMapsToTheSameKey()
        {
            // A duration can reach here after being stored in a float ReactiveProperty, so near-miss
            // values must not split one duration's leaderboard across two keys.
            Assert.AreEqual(TimedHighScoreKey.For(15f), TimedHighScoreKey.For(15.0f));
            Assert.AreEqual(TimedHighScoreKey.For(15f), TimedHighScoreKey.For(14.9999f));
            Assert.AreEqual(TimedHighScoreKey.For(15f), TimedHighScoreKey.For(15.0001f));
        }
    }
}
