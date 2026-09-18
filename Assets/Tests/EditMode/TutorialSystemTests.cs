using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers issue #278's core infrastructure: a trigger message queues (or immediately activates) the
    /// right coach-mark when unseen, the "seen" flag survives across a fresh System instance (the
    /// PlayerPrefs contract, not just the in-memory one), the one-shot already-unlocked-power-ups
    /// migration marks without ever enqueueing, and <see cref="TutorialSystem.NotifyTargetInteracted"/>
    /// only ever acknowledges an exact target match.
    /// </summary>
    public sealed class TutorialSystemTests
    {
        private const string MIGRATION_KEY = "Tutorial.Migration.AlreadyUnlockedPowerUps.Done";

        private TutorialModel _tutorialModel;
        private LevelProgressionModel _levelProgressionModel;
        private TestMessageBroker<PowerUpUnlockedMessage> _powerUpUnlockedBroker;
        private TestMessageBroker<SpecialCellSpawnedMessage> _specialCellSpawnedBroker;
        private TestMessageBroker<SpecialPieceSpawnedMessage> _specialPieceSpawnedBroker;
        private TestMessageBroker<HoldFirstUseMessage> _holdFirstUseBroker;
        private TutorialSystem _system;

        [SetUp]
        public void CreateSystem()
        {
            DeleteAllTutorialKeys();

            _tutorialModel = new TutorialModel();
            _levelProgressionModel = new LevelProgressionModel();
            _powerUpUnlockedBroker = new TestMessageBroker<PowerUpUnlockedMessage>();
            _specialCellSpawnedBroker = new TestMessageBroker<SpecialCellSpawnedMessage>();
            _specialPieceSpawnedBroker = new TestMessageBroker<SpecialPieceSpawnedMessage>();
            _holdFirstUseBroker = new TestMessageBroker<HoldFirstUseMessage>();

            _system = CreateTutorialSystem();
        }

        [TearDown]
        public void DisposeSystem()
        {
            _system.Dispose();
            DeleteAllTutorialKeys();
        }

        private TutorialSystem CreateTutorialSystem()
        {
            return new TutorialSystem(
                _tutorialModel,
                _levelProgressionModel,
                _powerUpUnlockedBroker,
                _specialCellSpawnedBroker,
                _specialPieceSpawnedBroker,
                _holdFirstUseBroker);
        }

        // --- (a) an unseen trigger activates a step ---

        [Test]
        public void HoldFirstUseMessage_Unseen_ActivatesTheHoldSlotStepImmediately()
        {
            _holdFirstUseBroker.Publish(new HoldFirstUseMessage());

            TutorialStep? active = _tutorialModel.ActiveStep.Value;
            Assert.IsNotNull(active);
            Assert.AreEqual(TutorialTargetId.HoldSlot, active.Value.TargetId);
        }

        [Test]
        public void SpecialCellSpawnedMessage_None_IsIgnored()
        {
            _specialCellSpawnedBroker.Publish(
                new SpecialCellSpawnedMessage(SpecialCellKind.None, new GridPosition(0, 0)));

            Assert.IsNull(_tutorialModel.ActiveStep.Value);
        }

        [Test]
        public void SpecialCellSpawnedMessage_Unseen_ActivatesABoardCellStepAtThatPosition()
        {
            var position = new GridPosition(3, 4);
            _specialCellSpawnedBroker.Publish(new SpecialCellSpawnedMessage(SpecialCellKind.Laser, position));

            TutorialStep? active = _tutorialModel.ActiveStep.Value;
            Assert.IsNotNull(active);
            Assert.AreEqual(TutorialTargetId.BoardCell, active.Value.TargetId);
            Assert.AreEqual(position, active.Value.BoardPosition);
        }

        // --- (b) the seen-flag persists across a fresh System instance ---

        [Test]
        public void Acknowledge_PersistsTheSeenFlag_SoAFreshSystemNeverReQueuesIt()
        {
            _holdFirstUseBroker.Publish(new HoldFirstUseMessage());
            Assert.IsNotNull(_tutorialModel.ActiveStep.Value);

            _system.Acknowledge();
            Assert.IsNull(_tutorialModel.ActiveStep.Value);

            // A brand new System, own Model, own brokers — the only thing carried over is PlayerPrefs.
            _system.Dispose();
            TutorialModel freshModel = new TutorialModel();
            TestMessageBroker<HoldFirstUseMessage> freshHoldBroker = new TestMessageBroker<HoldFirstUseMessage>();
            TutorialSystem freshSystem = new TutorialSystem(
                freshModel,
                _levelProgressionModel,
                _powerUpUnlockedBroker,
                _specialCellSpawnedBroker,
                _specialPieceSpawnedBroker,
                freshHoldBroker);

            freshHoldBroker.Publish(new HoldFirstUseMessage());

            Assert.IsNull(freshModel.ActiveStep.Value);
            freshSystem.Dispose();
        }

        // --- (c) migration marks already-unlocked power-ups seen without enqueueing ---

        [Test]
        public void Migration_OnFirstBootWithPowerUpsAlreadyUnlocked_MarksThemSeenWithoutEnqueueing()
        {
            // Simulate a returning player: dispose the SetUp system (built at level 1, before the
            // migration could see anything unlocked) and rebuild fresh at a level past several gates.
            _system.Dispose();
            DeleteAllTutorialKeys();
            _levelProgressionModel.CurrentLevelNumber.Value = 20; // Joker(5), ColorCleanser(10), Rotate(15), Reroll(20)

            _system = CreateTutorialSystem();

            Assert.AreEqual(1, PlayerPrefs.GetInt(MIGRATION_KEY, 0));

            // None of the newly-"already unlocked" kinds fire a coach-mark for the player.
            _powerUpUnlockedBroker.Publish(new PowerUpUnlockedMessage(PowerUpKind.Joker));
            Assert.IsNull(_tutorialModel.ActiveStep.Value);

            _powerUpUnlockedBroker.Publish(new PowerUpUnlockedMessage(PowerUpKind.Reroll));
            Assert.IsNull(_tutorialModel.ActiveStep.Value);

            // A kind not yet unlocked at that frontier still earns its coach-mark normally.
            _powerUpUnlockedBroker.Publish(new PowerUpUnlockedMessage(PowerUpKind.GhostFit));
            Assert.IsNotNull(_tutorialModel.ActiveStep.Value);
        }

        [Test]
        public void Migration_RunsExactlyOnce_SecondConstructionDoesNotReRun()
        {
            _system.Dispose();
            _levelProgressionModel.CurrentLevelNumber.Value = 5;
            _system = CreateTutorialSystem();
            Assert.AreEqual(1, PlayerPrefs.GetInt(MIGRATION_KEY, 0));

            // Manually clear Joker's seen-flag, as if the player had somehow reset it, then rebuild.
            // The migration must not run a second time and re-mark it.
            PlayerPrefs.DeleteKey(TutorialSeenKeyFor("PowerUpUnlocked_Joker"));
            _system.Dispose();
            _system = CreateTutorialSystem();

            _powerUpUnlockedBroker.Publish(new PowerUpUnlockedMessage(PowerUpKind.Joker));

            Assert.IsNotNull(_tutorialModel.ActiveStep.Value);
        }

        // --- (d) NotifyTargetInteracted only acknowledges an exact match ---

        [Test]
        public void NotifyTargetInteracted_WithANonMatchingTarget_DoesNothing()
        {
            _holdFirstUseBroker.Publish(new HoldFirstUseMessage());
            TutorialStep? beforeCall = _tutorialModel.ActiveStep.Value;

            _system.NotifyTargetInteracted(TutorialTargetId.BoardCell, new GridPosition(0, 0));

            Assert.AreEqual(beforeCall.Value.Id, _tutorialModel.ActiveStep.Value.Value.Id);
        }

        [Test]
        public void NotifyTargetInteracted_WithTheMatchingTarget_AcknowledgesAndPromotesTheQueuedStep()
        {
            // Two distinct spawns queue two distinct steps: the first activates immediately, the second
            // waits.
            var firstPosition = new GridPosition(1, 1);
            var secondPosition = new GridPosition(2, 2);
            _specialCellSpawnedBroker.Publish(new SpecialCellSpawnedMessage(SpecialCellKind.Laser, firstPosition));
            _specialCellSpawnedBroker.Publish(new SpecialCellSpawnedMessage(SpecialCellKind.Vortex, secondPosition));

            Assert.AreEqual(firstPosition, _tutorialModel.ActiveStep.Value.Value.BoardPosition);

            // A tap on some unrelated cell does not advance the queue.
            _system.NotifyTargetInteracted(TutorialTargetId.BoardCell, new GridPosition(7, 7));
            Assert.AreEqual(firstPosition, _tutorialModel.ActiveStep.Value.Value.BoardPosition);

            // The tap that matches the active step's own target acknowledges it and promotes the next.
            _system.NotifyTargetInteracted(TutorialTargetId.BoardCell, firstPosition);
            Assert.AreEqual(secondPosition, _tutorialModel.ActiveStep.Value.Value.BoardPosition);

            // Acknowledging the second, and last, queued step clears ActiveStep entirely.
            _system.NotifyTargetInteracted(TutorialTargetId.BoardCell, secondPosition);
            Assert.IsNull(_tutorialModel.ActiveStep.Value);
        }

        private static string TutorialSeenKeyFor(string stepId) => "Tutorial.Seen." + stepId;

        private static void DeleteAllTutorialKeys()
        {
            PlayerPrefs.DeleteKey(MIGRATION_KEY);
            PlayerPrefs.DeleteKey(TutorialSeenKeyFor("HoldFirstUse"));
            PlayerPrefs.DeleteKey(TutorialSeenKeyFor("PowerUpUnlocked_Joker"));
            PlayerPrefs.DeleteKey(TutorialSeenKeyFor("PowerUpUnlocked_ColorCleanser"));
            PlayerPrefs.DeleteKey(TutorialSeenKeyFor("PowerUpUnlocked_Rotate"));
            PlayerPrefs.DeleteKey(TutorialSeenKeyFor("PowerUpUnlocked_Reroll"));
            PlayerPrefs.DeleteKey(TutorialSeenKeyFor("PowerUpUnlocked_DoubleMultiplier"));
            PlayerPrefs.DeleteKey(TutorialSeenKeyFor("PowerUpUnlocked_GhostFit"));
            PlayerPrefs.DeleteKey(TutorialSeenKeyFor("PowerUpUnlocked_CoinSower"));
            PlayerPrefs.DeleteKey(TutorialSeenKeyFor("SpecialCellSpawned_Laser"));
            PlayerPrefs.DeleteKey(TutorialSeenKeyFor("SpecialCellSpawned_Vortex"));
        }
    }
}
