using System;
using System.Globalization;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="BadgeStatsModel"/>: the player's lifetime counters, folded up from the same
    /// gameplay messages everything else consumes and written straight back to PlayerPrefs.
    /// <para>
    /// Deliberately knows nothing about badges. It only counts; <see cref="BadgeSystem"/> is what
    /// decides that a count means something. That split is what lets a badge's threshold be retuned
    /// as a content edit without touching — or resetting — a single counter.
    /// </para>
    /// <para>
    /// One flat PlayerPrefs key per stat rather than a JSON blob: each counter moves independently and
    /// carries no cross-field invariant, so there is nothing here that needs writing atomically.
    /// <c>PlayerPrefs</c> has no <c>long</c> overload, so the values round-trip as invariant-culture
    /// strings — a device locale must not be able to change how a saved number reads back.
    /// </para>
    /// </summary>
    public sealed class BadgeStatsSystem : IDisposable
    {
        private readonly BadgeStatsModel _statsModel;
        private readonly IDisposable _subscriptions;

        public BadgeStatsSystem(
            BadgeStatsModel statsModel,
            ISubscriber<PiecePlacedMessage> piecePlacedSubscriber,
            ISubscriber<PowerUpAppliedMessage> powerUpAppliedSubscriber,
            ISubscriber<ScoreChangedMessage> scoreChangedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _statsModel = statsModel;

            // Loaded before subscribing so the counters are already whole by the time anything can
            // observe them — BadgeSystem reads them at its own construction and must not see zeroes.
            LoadAll();

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            piecePlacedSubscriber.Subscribe(OnPiecePlaced).AddTo(bag);
            powerUpAppliedSubscriber.Subscribe(OnPowerUpApplied).AddTo(bag);
            scoreChangedSubscriber.Subscribe(OnScoreChanged).AddTo(bag);
            runStartedSubscriber.Subscribe(OnRunStarted).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        /// <summary>One placement feeds three counters at once, which is why they share a message
        /// rather than each watching a narrower one: they must agree about what a placement was.</summary>
        private void OnPiecePlaced(PiecePlacedMessage message)
        {
            Bump(_statsModel.TotalPiecesPlaced, BadgeStatType.TotalPiecesPlaced, 1L);
            Bump(_statsModel.TotalLinesCleared, BadgeStatType.TotalLinesCleared, message.LinesCleared);

            if (message.BoardEmptyAfterPlacement)
            {
                Bump(_statsModel.TotalBoardWipes, BadgeStatType.TotalBoardWipes, 1L);
            }
        }

        private void OnPowerUpApplied(PowerUpAppliedMessage message)
        {
            // Counts the application, not the cells: a legally spent power-up that happened to hit an
            // empty row is still a power-up the player used.
            Bump(_statsModel.TotalPowerUpsApplied, BadgeStatType.TotalPowerUpsApplied, 1L);
        }

        /// <summary>
        /// Tracks the best run score ever as a high-water mark. Reading it off the score message rather
        /// than off game over means a record set mid-run counts immediately, and the run score dropping
        /// back to zero at the next run start cannot pull the mark down with it.
        /// </summary>
        private void OnScoreChanged(ScoreChangedMessage message)
        {
            if (message.Total <= _statsModel.HighestScoreEver.Value)
            {
                return;
            }

            _statsModel.HighestScoreEver.Value = message.Total;
            Persist(BadgeStatType.HighestScoreEver, _statsModel.HighestScoreEver.Value);
        }

        /// <summary>
        /// Every <see cref="RunStartedMessage"/> counts, including the one the scene publishes at boot.
        /// "Runs played" is deliberately "boards the player was given", not "boards the player lost":
        /// the boot run is a real, playable run, and excluding it would make the counter disagree with
        /// itself depending on whether the player quit mid-run or played on to game over.
        /// </summary>
        private void OnRunStarted(RunStartedMessage message)
        {
            Bump(_statsModel.TotalRunsPlayed, BadgeStatType.TotalRunsPlayed, 1L);
        }

        private void Bump(ReactiveProperty<long> counter, BadgeStatType statType, long delta)
        {
            if (delta <= 0L)
            {
                return;
            }

            counter.Value += delta;
            Persist(statType, counter.Value);
        }

        private void LoadAll()
        {
            _statsModel.TotalPiecesPlaced.Value = Read(BadgeStatType.TotalPiecesPlaced);
            _statsModel.TotalLinesCleared.Value = Read(BadgeStatType.TotalLinesCleared);
            _statsModel.TotalBoardWipes.Value = Read(BadgeStatType.TotalBoardWipes);
            _statsModel.HighestScoreEver.Value = Read(BadgeStatType.HighestScoreEver);
            _statsModel.TotalRunsPlayed.Value = Read(BadgeStatType.TotalRunsPlayed);
            _statsModel.TotalPowerUpsApplied.Value = Read(BadgeStatType.TotalPowerUpsApplied);
        }

        /// <summary>Reads one saved counter, treating anything unreadable as zero. A corrupt counter
        /// costs the player some progress towards a badge; refusing to boot costs them the game.</summary>
        private static long Read(BadgeStatType statType)
        {
            string stored = PlayerPrefs.GetString(BadgeStatsKey.For(statType), string.Empty);
            if (string.IsNullOrEmpty(stored))
            {
                return 0L;
            }

            if (!long.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                Debug.LogWarning($"Lifetime stat '{statType}' was unreadable ('{stored}') and has been reset to 0.");
                return 0L;
            }

            return Math.Max(0L, value);
        }

        private static void Persist(BadgeStatType statType, long value)
        {
            PlayerPrefs.SetString(BadgeStatsKey.For(statType), value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
