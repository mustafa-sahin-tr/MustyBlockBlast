using System;
using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="ScoreModel"/>. Reacts to placements only — it never sees the board and never
    /// references BoardSystem. All arithmetic comes from the injected <see cref="IScoreRule"/> set, so
    /// a new bonus is a new rule class plus one DI registration — this class does not change.
    /// <para>
    /// <see cref="ScoreModel.HighScore"/> is the persisted Endless best, so it is only ever bumped or
    /// saved while <see cref="GameModeSystem.CurrentMode"/> is <see cref="GameMode.Endless"/> — a good
    /// Timed run must never overwrite it (Timed keeps its own best in <see cref="TimedHighScoreSystem"/>).
    /// </para>
    /// </summary>
    public sealed class ScoreSystem : IDisposable
    {
        private const string HIGH_SCORE_PREFS_KEY = "Score.HighScore";

        private readonly ScoreModel _scoreModel;
        private readonly GameModeSystem _gameModeSystem;
        private readonly IScoreRule[] _scoreRules;
        private readonly IPublisher<ScoreChangedMessage> _scoreChangedPublisher;
        private readonly IPublisher<NewRecordMessage> _newRecordPublisher;
        private readonly IPublisher<BonusScoredMessage> _bonusScoredPublisher;
        private readonly IDisposable _subscriptions;

        /// <summary>High score the current run started with — the bar <see cref="NewRecordMessage"/> celebrates clearing.</summary>
        private int _recordAtRunStart;

        /// <summary>Set once <see cref="NewRecordMessage"/> has fired for the run in progress, so further gains don't re-trigger it.</summary>
        private bool _hasCelebratedRecordThisRun;

        public ScoreSystem(
            ScoreModel scoreModel,
            GameModeSystem gameModeSystem,
            IEnumerable<IScoreRule> scoreRules,
            ISubscriber<PiecePlacedMessage> piecePlacedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            IPublisher<ScoreChangedMessage> scoreChangedPublisher,
            IPublisher<NewRecordMessage> newRecordPublisher,
            IPublisher<BonusScoredMessage> bonusScoredPublisher)
        {
            _scoreModel = scoreModel;
            _gameModeSystem = gameModeSystem;

            // Materialised once so the per-placement loop never re-enumerates a lazy sequence.
            _scoreRules = new List<IScoreRule>(scoreRules).ToArray();
            _scoreChangedPublisher = scoreChangedPublisher;
            _newRecordPublisher = newRecordPublisher;
            _bonusScoredPublisher = bonusScoredPublisher;
            _scoreModel.HighScore.Value = PlayerPrefs.GetInt(HIGH_SCORE_PREFS_KEY, 0);
            _recordAtRunStart = _scoreModel.HighScore.Value;

            var bag = DisposableBag.CreateBuilder();
            piecePlacedSubscriber.Subscribe(OnPiecePlaced).AddTo(bag);
            runStartedSubscriber.Subscribe(OnRunStarted).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        private void OnRunStarted(RunStartedMessage message)
        {
            _scoreModel.Score.Value = 0;
            _scoreModel.Streak.Value = 0;
            _scoreModel.MultiClearStreak.Value = 0;
            _scoreModel.CumulativeMultiClearCount.Value = 0;
            _recordAtRunStart = _scoreModel.HighScore.Value;
            _hasCelebratedRecordThisRun = false;
            _scoreChangedPublisher.Publish(new ScoreChangedMessage(0, 0, 0));
        }

        private void OnPiecePlaced(PiecePlacedMessage message)
        {
            // Built before the streaks are touched: rules score against the streaks this placement began with.
            ScorePlacementContext context = new ScorePlacementContext(
                message.CellCount,
                message.LinesCleared,
                _scoreModel.Streak.Value,
                message.MonochromeLineCount,
                _scoreModel.MultiClearStreak.Value,
                _scoreModel.CumulativeMultiClearCount.Value,
                message.BoardEmptyAfterPlacement);

            int gained = 0;

            // Bonus-only subtotal, summed from the same ComputeBonus results as the grand total — it
            // exists purely so the presentation layer can celebrate bonuses (#61), and never feeds back
            // into the score itself.
            int bonusGained = 0;
            for (int ruleIndex = 0; ruleIndex < _scoreRules.Length; ruleIndex++)
            {
                IScoreRule rule = _scoreRules[ruleIndex];
                int ruleBonus = rule.ComputeBonus(context);
                gained += ruleBonus;

                if (rule is IBonusScoreRule)
                {
                    bonusGained += ruleBonus;
                }
            }

            if (message.LinesCleared > 0)
            {
                _scoreModel.Streak.Value += 1;
            }
            else
            {
                _scoreModel.Streak.Value = 0;
            }

            // The multi-clear streak only cares about 2+-line clears: a single breaks the chain, while a
            // non-clearing placement leaves it frozen (neither extended nor reset).
            if (message.LinesCleared >= 2)
            {
                _scoreModel.MultiClearStreak.Value += 1;

                // Counts the same trigger but never resets mid-run: it only tracks how many multi-clears the
                // run has accumulated, consecutive or not.
                _scoreModel.CumulativeMultiClearCount.Value += 1;
            }
            else if (message.LinesCleared == 1)
            {
                _scoreModel.MultiClearStreak.Value = 0;
            }

            _scoreModel.Score.Value += gained;

            if (_gameModeSystem.CurrentMode.Value == GameMode.Endless)
            {
                if (_scoreModel.Score.Value > _scoreModel.HighScore.Value)
                {
                    _scoreModel.HighScore.Value = _scoreModel.Score.Value;
                    PlayerPrefs.SetInt(HIGH_SCORE_PREFS_KEY, _scoreModel.HighScore.Value);
                }

                if (!_hasCelebratedRecordThisRun && _scoreModel.Score.Value > _recordAtRunStart)
                {
                    _hasCelebratedRecordThisRun = true;
                    _newRecordPublisher.Publish(new NewRecordMessage());
                }
            }

            _scoreChangedPublisher.Publish(new ScoreChangedMessage(
                _scoreModel.Score.Value, gained, _scoreModel.Streak.Value));

            // Nothing is published for a bonus-free placement, so consumers never have to filter zeroes.
            if (bonusGained > 0)
            {
                _bonusScoredPublisher.Publish(new BonusScoredMessage(bonusGained));
            }
        }
    }
}
