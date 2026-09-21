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
        private readonly DoubleMultiplierModel _doubleMultiplierModel;
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
            DoubleMultiplierModel doubleMultiplierModel,
            IEnumerable<IScoreRule> scoreRules,
            ISubscriber<PiecePlacedMessage> piecePlacedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            IPublisher<ScoreChangedMessage> scoreChangedPublisher,
            IPublisher<NewRecordMessage> newRecordPublisher,
            IPublisher<BonusScoredMessage> bonusScoredPublisher)
        {
            _scoreModel = scoreModel;
            _gameModeSystem = gameModeSystem;
            _doubleMultiplierModel = doubleMultiplierModel;

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

        /// <summary>
        /// Adds a <see cref="GameMode.Path"/> level-completion bonus to the run in progress and returns
        /// the run's new total.
        /// <para>
        /// A method rather than a rule because it is not one: <see cref="IScoreRule"/> scores a
        /// placement, and this is paid for clearing a level — there is no placement context to hand a
        /// rule, and the payment happens once per run rather than once per piece.
        /// </para>
        /// <para>
        /// Deliberately leaves both streaks alone: a bonus is not a placement, so it neither extends
        /// nor breaks a combo. Equally deliberately, it is not doubled by the 2x frenzy — the level
        /// reward is a fixed authored figure, not points the player played for.
        /// </para>
        /// <para>
        /// The Endless high score is untouched, which needs no branch here: this is only ever called in
        /// Path mode, and the persisted best is only written from the Endless branch of
        /// <see cref="OnPiecePlaced"/>.
        /// </para>
        /// Returns the current total unchanged for a zero (or negative) bonus, so an unauthored level
        /// publishes nothing.
        /// </summary>
        internal int AddLevelCompletionBonus(int amount)
        {
            if (amount <= 0)
            {
                return _scoreModel.Score.Value;
            }

            _scoreModel.Score.Value += amount;
            _scoreChangedPublisher.Publish(new ScoreChangedMessage(
                _scoreModel.Score.Value, amount, _scoreModel.Streak.Value));

            return _scoreModel.Score.Value;
        }

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

            // Applied to the finished total, not to any individual rule: the whole additive stack —
            // placement, clears, streak and milestone bonuses — is computed exactly as it always is,
            // and only its output is doubled. Zero stays zero, so a placement that scored nothing still
            // scores nothing inside a frenzy. The bonus subtotal is doubled with it so the celebration
            // the player sees matches the points they were actually credited.
            gained = _doubleMultiplierModel.Multiply(gained);
            bonusGained = _doubleMultiplierModel.Multiply(bonusGained);

            // Read before the gem multiplies the total, so the delta below is exactly what the gem
            // itself was worth on this placement — base score and every rule bonus alike, whichever mix
            // produced it.
            int gainedBeforeScoreGem = gained;

            // Layered on top of the frenzy rather than replacing it: each multiplier is applied to the
            // running total in turn, so a gem destroyed inside a 2x window is worth 6x. Zero stays zero
            // here too — there is no floor — so a placement that scored nothing scores nothing however
            // many gems it happened to sweep up. The bonus subtotal is tripled with it for the same
            // reason it is doubled with it: the celebration must match the points actually credited.
            gained = ScoreRules.ScoreGemMultiplied(gained, message.DestroyedScoreGemCount);
            bonusGained = ScoreRules.ScoreGemMultiplied(bonusGained, message.DestroyedScoreGemCount);

            // The gem's own contribution — everything the multiplier added on top of what this
            // placement already earned — folded into the same "extra credited this placement" figure
            // the bonus popup shows (issue: "score gem silindiğinde... +30 gibi... gösterir"). Never
            // negative (the multiplier only ever scales up or leaves the total unchanged), and 0 when
            // no gem was destroyed, so a gem-free placement's popup is untouched by this.
            int scoreGemBonus = gained - gainedBeforeScoreGem;
            bonusGained += scoreGemBonus;

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
