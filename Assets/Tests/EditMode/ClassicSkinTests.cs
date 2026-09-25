using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Issue #333 (as refined in #476): in Classic the whole board wears one skin at a time and moves on
    /// to the next stage of the configured sequence as the run's score reaches each threshold. Path and
    /// Şölen keep the colour blocks; every run starts on them; a stage never goes back mid-run.
    /// </summary>
    public sealed class ClassicSkinTests
    {
        private const string SEQUENCE_JSON =
            "{\"_stages\":[{\"_name\":\"Colours\",\"_scoreThreshold\":0},{\"_name\":\"Jelly\",\"_scoreThreshold\":2000},"
            + "{\"_name\":\"Fruit\",\"_scoreThreshold\":5000},{\"_name\":\"Wood\",\"_scoreThreshold\":10000}]}";

        [TestCase(0, 0)]
        [TestCase(1999, 0)]
        [TestCase(2000, 1)]
        [TestCase(7000, 2)]
        [TestCase(10000, 3)]
        [TestCase(10249, 3)]
        [TestCase(10250, 4)]
        [TestCase(10750, 6)]
        public void StageIndexFor_IsTheLastThresholdReached_ThenOneStepPerLoopInterval(int score, int expected)
        {
            Assert.AreEqual(expected, ASequence().StageIndexFor(score));
        }

        /// <summary>Past the last authored stage the sequence starts over from stage 1 — never the colour
        /// blocks — so a long run keeps getting new skins.</summary>
        [Test]
        public void StageAt_PastTheEnd_CyclesThroughTheSkinsAgain()
        {
            ClassicSkinConfig config = ASequence();

            Assert.AreEqual("Jelly", config.StageAt(4).Name);
            Assert.AreEqual("Fruit", config.StageAt(5).Name);
            Assert.AreEqual("Wood", config.StageAt(6).Name);
            Assert.AreEqual("Jelly", config.StageAt(7).Name);
        }

        [Test]
        public void InAClassicRun_TheScoreWalksTheSequence_AndNeverGoesBack()
        {
            ClassicSkinSystem system = CreateSystem(
                GameMode.Timed, out ClassicSkinModel skinModel, out ScoreModel scoreModel, out _);

            scoreModel.Score.Value = 2500;
            Assert.AreEqual(1, skinModel.StageIndex.Value);
            scoreModel.Score.Value = 10000;
            Assert.AreEqual(3, skinModel.StageIndex.Value);
            scoreModel.Score.Value = 100;
            Assert.AreEqual(3, skinModel.StageIndex.Value, "A lower score never brings a skin back.");
            system.Dispose();
        }

        [Test]
        public void ANewRun_StartsBackOnTheColourBlocks()
        {
            ClassicSkinSystem system = CreateSystem(
                GameMode.Timed, out ClassicSkinModel skinModel, out ScoreModel scoreModel,
                out TestMessageBroker<RunStartedMessage> runStarted);
            scoreModel.Score.Value = 6000;
            Assert.AreEqual(2, skinModel.StageIndex.Value);

            runStarted.Publish(new RunStartedMessage());

            Assert.AreEqual(0, skinModel.StageIndex.Value);
            system.Dispose();
        }

        [TestCase(GameMode.Path)]
        [TestCase(GameMode.Endless)]
        public void OutsideClassic_TheBoardKeepsTheColourBlocks(GameMode mode)
        {
            ClassicSkinSystem system = CreateSystem(mode, out ClassicSkinModel skinModel, out ScoreModel scoreModel, out _);

            scoreModel.Score.Value = 50000;

            Assert.AreEqual(0, skinModel.StageIndex.Value);
            system.Dispose();
        }

        private static ClassicSkinConfig ASequence()
        {
            var config = ScriptableObject.CreateInstance<ClassicSkinConfig>();
            JsonUtility.FromJsonOverwrite(SEQUENCE_JSON, config);
            return config;
        }

        private static ClassicSkinSystem CreateSystem(
            GameMode mode,
            out ClassicSkinModel skinModel,
            out ScoreModel scoreModel,
            out TestMessageBroker<RunStartedMessage> runStarted)
        {
            skinModel = new ClassicSkinModel();
            scoreModel = new ScoreModel();
            var gameModeModel = new GameModeModel();
            gameModeModel.CurrentMode.Value = mode;
            runStarted = new TestMessageBroker<RunStartedMessage>();
            return new ClassicSkinSystem(skinModel, ASequence(), scoreModel, gameModeModel, runStarted);
        }
    }
}
