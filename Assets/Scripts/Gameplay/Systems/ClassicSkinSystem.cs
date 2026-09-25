using System;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Walks the Classic skin sequence (issue #333): every run starts on the colour blocks, and in a
    /// Classic (Timed) run each score threshold the run reaches moves the whole board on to that stage's
    /// skin. The stage only moves forward within a run — a skin never flickers back. In Path and Şölen the
    /// board stays on the colour blocks. Cosmetic only: nothing here touches a rule.
    /// </summary>
    public sealed class ClassicSkinSystem : IDisposable
    {
        private readonly ClassicSkinModel _skinModel;
        private readonly ClassicSkinConfig _config;
        private readonly ScoreModel _scoreModel;
        private readonly GameModeModel _gameModeModel;
        private readonly IDisposable _subscriptions;
        private readonly IDisposable _scoreSubscription;

        public ClassicSkinSystem(
            ClassicSkinModel skinModel,
            ClassicSkinConfig config,
            ScoreModel scoreModel,
            GameModeModel gameModeModel,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _skinModel = skinModel;
            _config = config;
            _scoreModel = scoreModel;
            _gameModeModel = gameModeModel;

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            runStartedSubscriber.Subscribe(OnRunStarted).AddTo(bag);
            _subscriptions = bag.Build();
            _scoreSubscription = _scoreModel.Score.Subscribe(OnScoreChanged);
        }

        public void Dispose()
        {
            _subscriptions.Dispose();
            _scoreSubscription.Dispose();
        }

        private void OnRunStarted(RunStartedMessage message) => _skinModel.StageIndex.Value = 0;

        private void OnScoreChanged(int score)
        {
            if (_config == null || _gameModeModel.CurrentMode.Value != GameMode.Timed)
            {
                if (_skinModel.StageIndex.Value != 0)
                {
                    _skinModel.StageIndex.Value = 0;
                }

                return;
            }

            int stageIndex = _config.StageIndexFor(score);
            if (stageIndex > _skinModel.StageIndex.Value)
            {
                _skinModel.StageIndex.Value = stageIndex;
            }
        }
    }
}
