using MustyBlockBlast.Core;
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
    /// Covers #195: the active <see cref="GameMode"/> survives a relaunch. Each test constructs a
    /// fresh <see cref="GameModeSystem"/> over the real <c>PlayerPrefs</c> key it persists to, standing
    /// in for the app being closed and reopened — a new instance reading whatever the previous one
    /// wrote is exactly what a cold boot does.
    /// </summary>
    public class GameModeSystemPersistenceTests
    {
        private const string GAME_MODE_KEY = "Settings.GameMode";

        [SetUp]
        public void ClearPersistedMode() => PlayerPrefs.DeleteKey(GAME_MODE_KEY);

        [TearDown]
        public void ClearPersistedModeAfterwards() => PlayerPrefs.DeleteKey(GAME_MODE_KEY);

        // --- AC3: fresh install ---

        [Test]
        public void Construct_WithNoSavedMode_DefaultsToEndless()
        {
            GameModeSystem system = new GameModeSystem(new GameModeModel(), CreateBoardSystem());

            Assert.AreEqual(GameMode.Endless, system.CurrentMode.Value);
        }

        // --- AC1/AC2: a selected mode is restored on the next boot ---

        [TestCase(GameMode.Timed)]
        [TestCase(GameMode.Path)]
        public void Construct_AfterASavedSelection_RestoresThatMode(GameMode savedMode)
        {
            GameModeSystem firstBoot = new GameModeSystem(new GameModeModel(), CreateBoardSystem());
            firstBoot.SelectMode(savedMode);

            GameModeSystem secondBoot = new GameModeSystem(new GameModeModel(), CreateBoardSystem());

            Assert.AreEqual(savedMode, secondBoot.CurrentMode.Value);
        }

        // --- AC4: the last selection before quitting wins, not an earlier one ---

        [Test]
        public void Construct_AfterSwitchingModeMultipleTimes_RestoresOnlyTheLastOne()
        {
            GameModeSystem firstBoot = new GameModeSystem(new GameModeModel(), CreateBoardSystem());
            firstBoot.SelectMode(GameMode.Timed);
            firstBoot.SelectMode(GameMode.Endless);

            GameModeSystem secondBoot = new GameModeSystem(new GameModeModel(), CreateBoardSystem());

            Assert.AreEqual(GameMode.Endless, secondBoot.CurrentMode.Value);
        }

        // --- AC5: restoring a mode at construction must not itself start a run ---

        [Test]
        public void Construct_WithASavedNonEndlessMode_DoesNotStartARun()
        {
            GameModeSystem firstBoot = new GameModeSystem(new GameModeModel(), CreateBoardSystem());
            firstBoot.SelectMode(GameMode.Path);

            TestMessageBroker<RunStartedMessage> runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            BoardSystem boardSystem = CreateBoardSystem(runStartedBroker);

            GameModeSystem secondBoot = new GameModeSystem(new GameModeModel(), boardSystem);

            Assert.AreEqual(GameMode.Path, secondBoot.CurrentMode.Value);
            Assert.IsEmpty(runStartedBroker.Published);
        }

        // --- AC6: an unset/corrupted saved value falls back to Endless rather than throwing ---

        [Test]
        public void Construct_WithACorruptedSavedValue_FallsBackToEndless()
        {
            PlayerPrefs.SetInt(GAME_MODE_KEY, 999);

            GameModeSystem system = new GameModeSystem(new GameModeModel(), CreateBoardSystem());

            Assert.AreEqual(GameMode.Endless, system.CurrentMode.Value);
        }

        /// <summary>A real, unstarted board system. Nothing here places a piece through it; it exists
        /// only because <see cref="GameModeSystem"/> needs one to construct and, for AC5, to prove it
        /// was never asked to start a run.</summary>
        private static BoardSystem CreateBoardSystem(TestMessageBroker<RunStartedMessage> runStartedBroker = null)
        {
            return new BoardSystem(
                new BoardModel(),
                new TrayModel(),
                new ScoreGemProgressModel(),
                new WeightedPieceDraw(),
                runStartedBroker ?? new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexPulledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                reinforcedCellSeeder: null);
        }
    }
}
