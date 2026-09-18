using System;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Decides which coach-mark, if any, a game event earns; queues it behind whatever is already
    /// showing; and persists which steps the player has already acknowledged so none is ever shown
    /// twice. The one and only writer of <see cref="TutorialModel"/> and the one and only reader/writer
    /// of the "seen" flags (see <see cref="TutorialSeenKey"/>), mirroring how <see cref="PowerUpSystem"/>
    /// is the one owner of the power-up inventory it persists.
    /// <para>
    /// Listens to four triggers, each of which names a genuine, one-time-per-instance game event:
    /// <see cref="PowerUpUnlockedMessage"/> (a power-up's level gate was just crossed),
    /// <see cref="SpecialCellSpawnedMessage"/> (a special cell was just painted onto the board),
    /// <see cref="SpecialPieceSpawnedMessage"/> (a special piece was just injected into the dock) and
    /// <see cref="HoldFirstUseMessage"/> (published on every Hold use — "first" is decided here, by the
    /// seen-flag, not by the publisher).
    /// </para>
    /// <para>
    /// <b>Migration.</b> A power-up already unlocked (by level) when this feature first ships must not
    /// suddenly show a coach-mark for something the player has had all along, and must not be silently
    /// forgotten either — it is marked seen outright, once, via <see cref="RunAlreadyUnlockedPowerUpMigration"/>,
    /// gated by its own one-shot flag so it can never re-run and re-mark a kind the player has since
    /// (legitimately) had reset.
    /// </para>
    /// </summary>
    public sealed class TutorialSystem : IDisposable
    {
        private static readonly PowerUpKind[] AllPowerUpKinds =
        {
            PowerUpKind.Bomb, PowerUpKind.RowClear, PowerUpKind.ColumnClear, PowerUpKind.Joker,
            PowerUpKind.ColorCleanser, PowerUpKind.Rotate, PowerUpKind.Reroll,
            PowerUpKind.DoubleMultiplier, PowerUpKind.GhostFit, PowerUpKind.CoinSower, PowerUpKind.Hold,
        };

        private const string HOLD_FIRST_USE_STEP_ID = "HoldFirstUse";

        private readonly TutorialModel _tutorialModel;

        private readonly IDisposable _powerUpUnlockedSubscription;
        private readonly IDisposable _specialCellSpawnedSubscription;
        private readonly IDisposable _specialPieceSpawnedSubscription;
        private readonly IDisposable _holdFirstUseSubscription;

        [Inject]
        public TutorialSystem(
            TutorialModel tutorialModel,
            LevelProgressionModel levelProgressionModel,
            ISubscriber<PowerUpUnlockedMessage> powerUpUnlockedSubscriber,
            ISubscriber<SpecialCellSpawnedMessage> specialCellSpawnedSubscriber,
            ISubscriber<SpecialPieceSpawnedMessage> specialPieceSpawnedSubscriber,
            ISubscriber<HoldFirstUseMessage> holdFirstUseSubscriber)
        {
            _tutorialModel = tutorialModel;

            RunAlreadyUnlockedPowerUpMigration(levelProgressionModel);

            _powerUpUnlockedSubscription = powerUpUnlockedSubscriber.Subscribe(OnPowerUpUnlocked);
            _specialCellSpawnedSubscription = specialCellSpawnedSubscriber.Subscribe(OnSpecialCellSpawned);
            _specialPieceSpawnedSubscription = specialPieceSpawnedSubscriber.Subscribe(OnSpecialPieceSpawned);
            _holdFirstUseSubscription = holdFirstUseSubscriber.Subscribe(OnHoldFirstUse);
        }

        public void Dispose()
        {
            _powerUpUnlockedSubscription.Dispose();
            _specialCellSpawnedSubscription.Dispose();
            _specialPieceSpawnedSubscription.Dispose();
            _holdFirstUseSubscription.Dispose();
        }

        /// <summary>
        /// Called by whichever View just handled a press that landed on <paramref name="targetId"/>
        /// (with its payload), after handling it normally. A no-op unless a step is active and this is
        /// exactly its target — callers do not need to know whether a tutorial is running at all, only
        /// to report what they just did.
        /// </summary>
        public void NotifyTargetInteracted(
            TutorialTargetId targetId, GridPosition? boardPosition = null, int slotIndex = -1)
        {
            TutorialStep? active = _tutorialModel.ActiveStep.Value;
            if (active == null || !active.Value.Matches(targetId, boardPosition, slotIndex))
            {
                return;
            }

            Acknowledge();
        }

        /// <summary>Marks the active step seen and promotes the next queued one (or clears
        /// <see cref="TutorialModel.ActiveStep"/> when none is left). A no-op when nothing is active.</summary>
        public void Acknowledge()
        {
            TutorialStep? active = _tutorialModel.ActiveStep.Value;
            if (active == null)
            {
                return;
            }

            MarkSeen(active.Value.Id);
            PromoteNextStep();
        }

        private void OnPowerUpUnlocked(PowerUpUnlockedMessage message)
        {
            string stepId = PowerUpUnlockedStepId(message.Kind);
            TutorialStep step = new TutorialStep(
                stepId,
                TutorialTargetId.PowerUpStripSlot,
                LocalizationKeys.TUTORIAL_POWERUP_UNLOCKED,
                TutorialArrowDirection.Down,
                slotIndex: (int)message.Kind);
            Enqueue(step);
        }

        private void OnSpecialCellSpawned(SpecialCellSpawnedMessage message)
        {
            if (message.Kind == SpecialCellKind.None)
            {
                return;
            }

            string stepId = "SpecialCellSpawned_" + message.Kind;
            TutorialStep step = new TutorialStep(
                stepId,
                TutorialTargetId.BoardCell,
                LocalizationKeys.TUTORIAL_SPECIAL_CELL_SPAWNED,
                TutorialArrowDirection.Down,
                boardPosition: message.Position);
            Enqueue(step);
        }

        private void OnSpecialPieceSpawned(SpecialPieceSpawnedMessage message)
        {
            if (message.Kind == SpecialPieceKind.None)
            {
                return;
            }

            string stepId = "SpecialPieceSpawned_" + message.Kind;
            TutorialStep step = new TutorialStep(
                stepId,
                TutorialTargetId.TraySlot,
                LocalizationKeys.TUTORIAL_SPECIAL_PIECE_SPAWNED,
                TutorialArrowDirection.Up,
                slotIndex: message.SlotIndex);
            Enqueue(step);
        }

        private void OnHoldFirstUse(HoldFirstUseMessage message)
        {
            if (IsSeen(HOLD_FIRST_USE_STEP_ID))
            {
                // Every use publishes this, not only the first — this is what makes it a no-op from
                // the second acknowledged use onward.
                return;
            }

            TutorialStep step = new TutorialStep(
                HOLD_FIRST_USE_STEP_ID,
                TutorialTargetId.HoldSlot,
                LocalizationKeys.TUTORIAL_HOLD_FIRST_USE,
                TutorialArrowDirection.Up);
            Enqueue(step);
        }

        /// <summary>Queues <paramref name="step"/> behind whatever is already showing, or promotes it
        /// immediately when nothing is. Skipped outright when the step's id was already acknowledged.</summary>
        private void Enqueue(TutorialStep step)
        {
            if (IsSeen(step.Id))
            {
                return;
            }

            if (_tutorialModel.ActiveStep.Value == null && _tutorialModel.PendingSteps.Count == 0)
            {
                _tutorialModel.ActiveStep.Value = step;
            }
            else
            {
                _tutorialModel.PendingSteps.Enqueue(step);
            }
        }

        private void PromoteNextStep()
        {
            _tutorialModel.ActiveStep.Value = _tutorialModel.PendingSteps.Count > 0
                ? _tutorialModel.PendingSteps.Dequeue()
                : (TutorialStep?)null;
        }

        /// <summary>
        /// Marks every <see cref="PowerUpKind"/> already unlocked at the player's current progression
        /// frontier as seen, without ever enqueueing a step for them — the whole point being that a
        /// returning player is not shown a coach-mark for something they already have. Gated by its own
        /// one-shot flag so it runs exactly once, on the first boot after this feature ships.
        /// </summary>
        private static void RunAlreadyUnlockedPowerUpMigration(LevelProgressionModel levelProgressionModel)
        {
            if (PlayerPrefs.GetInt(TutorialSeenKey.ALREADY_UNLOCKED_MIGRATION_DONE, 0) != 0)
            {
                return;
            }

            int currentLevelNumber = levelProgressionModel.CurrentLevelNumber.Value;
            for (int kindIndex = 0; kindIndex < AllPowerUpKinds.Length; kindIndex++)
            {
                PowerUpKind kind = AllPowerUpKinds[kindIndex];
                if (PowerUpUnlockLevels.IsUnlockedAt(kind, currentLevelNumber))
                {
                    MarkSeen(PowerUpUnlockedStepId(kind));
                }
            }

            PlayerPrefs.SetInt(TutorialSeenKey.ALREADY_UNLOCKED_MIGRATION_DONE, 1);
            PlayerPrefs.Save();
        }

        private static string PowerUpUnlockedStepId(PowerUpKind kind) => "PowerUpUnlocked_" + kind;

        private static bool IsSeen(string stepId) => PlayerPrefs.GetInt(TutorialSeenKey.For(stepId), 0) != 0;

        private static void MarkSeen(string stepId)
        {
            PlayerPrefs.SetInt(TutorialSeenKey.For(stepId), 1);
            PlayerPrefs.Save();
        }
    }
}
