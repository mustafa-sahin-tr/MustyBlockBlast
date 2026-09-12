using System;
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
    /// references BoardSystem. All arithmetic comes from <see cref="ScoreRules"/>.
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
        private readonly IPublisher<ScoreChangedMessage> _scoreChangedPublisher;
        private readonly IPublisher<NewRecordMessage> _newRecordPublisher;
        private readonly IDisposable _subscriptions;

        /// <summary>High score the current run started with — the bar <see cref="NewRecordMessage"/> celebrates clearing.</summary>
        private int _recordAtRunStart;

        /// <summary>Set once <see cref="NewRecordMessage"/> has fired for the run in progress, so further gains don't re-trigger it.</summary>
        private bool _hasCelebratedRecordThisRun;

        public ScoreSystem(
            ScoreModel scoreModel,
            GameModeSystem gameModeSystem,
            ISubscriber<PiecePlacedMessage> piecePlacedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            IPublisher<ScoreChangedMessage> scoreChangedPublisher,
            IPublisher<NewRecordMessage> newRecordPublisher)
        {
            _scoreModel = scoreModel;
            _gameModeSystem = gameModeSystem;
            _scoreChangedPublisher = scoreChangedPublisher;
            _newRecordPublisher = newRecordPublisher;
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
            _recordAtRunStart = _scoreModel.HighScore.Value;
            _hasCelebratedRecordThisRun = false;
            _scoreChangedPublisher.Publish(new ScoreChangedMessage(0, 0, 0));
        }

        private void OnPiecePlaced(PiecePlacedMessage message)
        {
            int gained = ScoreRules.PlacementScore(message.CellCount);

            if (message.LinesCleared > 0)
            {
                gained += ScoreRules.ClearScore(message.LinesCleared, _scoreModel.Streak.Value);
                _scoreModel.Streak.Value += 1;
            }
            else
            {
                _scoreModel.Streak.Value = 0;
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
        }
    }
}
