using System;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Scores power-up clears. Kept apart from <see cref="ScoreSystem"/> because the streak counters
    /// are a placement concept: a power-up is not a placement, so it must never extend, reset or
    /// otherwise touch <see cref="ScoreModel.Streak"/>, <see cref="ScoreModel.MultiClearStreak"/> or
    /// <see cref="ScoreModel.CumulativeMultiClearCount"/>. It reads the streak, never writes it.
    /// <para>
    /// A bomb pays per cell (<see cref="ScoreRules.PlacementScore"/>) because its yield already scales
    /// with how much it destroyed; a row/column clear pays the one-line clear rate, so it is worth the
    /// same as earning that line the hard way. A colour cleanser follows the bomb's rule, not the
    /// row/column one: its cleared region has no fixed shape or size (a rare colour might clear 1
    /// cell, a common one most of the board), so a flat one-line rate would pay the same regardless of
    /// how much it actually destroyed — exactly the mismatch the bomb's per-cell rate exists to avoid.
    /// </para>
    /// <para>
    /// A joker pays the clear rate for however many lines it actually completed (one, or two when the
    /// filled cell closed its row and column at once) — again the same as earning them the hard way.
    /// A joker that only filled a cell pays nothing: it cleared nothing, and the general "cleared
    /// nothing, scored nothing" rule below already covers it.
    /// </para>
    /// </summary>
    public sealed class PowerUpScoreSystem : IDisposable
    {
        private const int POWER_UP_LINES_CLEARED = 1;

        private readonly ScoreModel _scoreModel;
        private readonly DoubleMultiplierModel _doubleMultiplierModel;
        private readonly IPublisher<ScoreChangedMessage> _scoreChangedPublisher;
        private readonly IDisposable _subscription;

        public PowerUpScoreSystem(
            ScoreModel scoreModel,
            DoubleMultiplierModel doubleMultiplierModel,
            ISubscriber<PowerUpAppliedMessage> powerUpAppliedSubscriber,
            IPublisher<ScoreChangedMessage> scoreChangedPublisher)
        {
            _scoreModel = scoreModel;
            _doubleMultiplierModel = doubleMultiplierModel;
            _scoreChangedPublisher = scoreChangedPublisher;
            _subscription = powerUpAppliedSubscriber.Subscribe(OnPowerUpApplied);
        }

        public void Dispose() => _subscription.Dispose();

        private void OnPowerUpApplied(PowerUpAppliedMessage message)
        {
            // A power-up spent on an empty target is still consumed, but it cleared nothing, so there
            // is nothing to pay for and nothing for the HUD to react to.
            if (message.ClearedCellCount <= 0)
            {
                return;
            }

            int gained = GainFor(message);
            if (gained <= 0)
            {
                return;
            }

            // Doubled on the way out, like a placement's total: a frenzy applies to every score gain in
            // the run, not just to the ones that came from placing a piece. The zero case never reaches
            // here — the early-out above already dropped it — so nothing is published for a clear that
            // was worth nothing, doubled or not.
            gained = _doubleMultiplierModel.Multiply(gained);

            // Layered on top of the frenzy rather than replacing it, exactly as a placement's is: a gem
            // destroyed by a spent power-up inside a 2x window is worth 6x. The zero case never reaches
            // here — the early-out above already dropped it — so there is nothing for the (floorless)
            // tripling to turn into points out of nowhere.
            gained = ScoreRules.ScoreGemMultiplied(gained, message.DestroyedScoreGemCount);

            _scoreModel.Score.Value += gained;

            _scoreChangedPublisher.Publish(new ScoreChangedMessage(
                _scoreModel.Score.Value, gained, _scoreModel.Streak.Value));
        }

        /// <summary>
        /// The points one applied power-up is worth. The streak is read into the multiplier exactly as
        /// a placement's clear would read it, and never written — that asymmetry is the whole reason
        /// this system exists apart from <see cref="ScoreSystem"/>.
        /// </summary>
        private int GainFor(PowerUpAppliedMessage message)
        {
            switch (message.Kind)
            {
                case PowerUpKind.Bomb:
                case PowerUpKind.ColorCleanser:
                    return ScoreRules.PlacementScore(message.ClearedCellCount);
                case PowerUpKind.Joker:
                    // The only kind that can clear more than one line at a time, so it is the only one
                    // that pays from the message rather than from a fixed count.
                    return ScoreRules.ClearScore(message.ClearedLineCount, _scoreModel.Streak.Value);
                default:
                    return ScoreRules.ClearScore(POWER_UP_LINES_CLEARED, _scoreModel.Streak.Value);
            }
        }
    }
}
