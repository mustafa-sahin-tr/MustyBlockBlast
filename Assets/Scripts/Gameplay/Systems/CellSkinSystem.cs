using System;
using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Drives the cosmetic cell-skin feature (issue #324, sub-issue A): a Classic-mode
    /// (<see cref="GameMode.Timed"/>) run's score progressively unlocks the four themes in
    /// <see cref="CellSkinKind"/> declaration order as it crosses each of
    /// <see cref="CellSkinConfig.ThemeUnlockThresholds"/> — only Cake at the first threshold, Cake or
    /// Candy at the second, and so on — converting a few currently-occupied, still-plain board cells to
    /// a theme rolled uniformly from whatever is unlocked so far each time a threshold is crossed. Once
    /// every theme is unlocked (the last threshold is crossed), every further multiple of
    /// <see cref="CellSkinConfig.RepeatIntervalPoints"/> triggers one more such batch, themed from the
    /// full four-way pool. From the very first batch onward, for the rest of the run, every newly drawn
    /// tray piece also comes pre-themed (<see cref="DrawSkinForNewPiece"/>, called by
    /// <see cref="BoardSystem"/>'s refill/injection paths), rolled from the same currently-unlocked pool.
    /// <para>
    /// Its own System rather than another branch of <see cref="BoardSystem"/>, for the reason
    /// <see cref="LaserSpawnSystem"/>/<see cref="CoinStreakTriggerSystem"/> are their own: the trigger
    /// is a score concept layered on top of the board, not a placement-resolution concern.
    /// </para>
    /// <para>
    /// Gated on <see cref="GameModeModel.SkinsEnabled"/> — deliberately not
    /// <see cref="GameModeModel.ExtrasEnabled"/> — everywhere: the unlock schedule, the score-threshold
    /// conversion, the per-piece draw, all of it. Every mode other than Classic never assigns a skin
    /// (issue #324 AC0/AC8).
    /// </para>
    /// </summary>
    public sealed class CellSkinSystem : IDisposable
    {
        private readonly BoardModel _boardModel;
        private readonly GameModeModel _gameModeModel;
        private readonly CellSkinConfig _config;
        private readonly IPublisher<CellSkinAppliedMessage> _cellSkinAppliedPublisher;

        /// <summary>Picks which occupied cells convert and which theme any conversion or draw rolls,
        /// uniformly among however many themes are unlocked so far. Whether a threshold has been crossed
        /// at all is fully deterministic (score-driven) and never touches this.</summary>
        private readonly Random _random;

        private readonly IDisposable _subscription;

        /// <summary>How many of the four themes this run has unlocked so far, in
        /// <see cref="CellSkinKind"/> declaration order — 0 until the first threshold is crossed, at
        /// most 4. Every roll (conversion or draw) picks uniformly among the first this-many
        /// <see cref="CellSkinKind"/> values. Reset to 0 by <see cref="ResetRun"/>.</summary>
        private int _unlockedThemeCount;

        /// <summary>How many repeat-interval batches beyond the last unlock threshold this run has
        /// already triggered. Tracked separately from <see cref="_unlockedThemeCount"/> because crossing
        /// the last threshold unlocks a theme AND fires its own batch, while every repeat afterwards
        /// fires a batch without unlocking anything further. Reset to 0 by <see cref="ResetRun"/>.
        /// </summary>
        private int _repeatBatchesTriggered;

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        [Inject]
        public CellSkinSystem(
            BoardModel boardModel,
            ScoreModel scoreModel,
            CellSkinConfig config,
            IPublisher<CellSkinAppliedMessage> cellSkinAppliedPublisher = null,
            GameModeModel gameModeModel = null)
            : this(boardModel, scoreModel, config, Environment.TickCount, cellSkinAppliedPublisher, gameModeModel)
        {
        }

        internal CellSkinSystem(
            BoardModel boardModel,
            ScoreModel scoreModel,
            CellSkinConfig config,
            int seed,
            IPublisher<CellSkinAppliedMessage> cellSkinAppliedPublisher = null,
            GameModeModel gameModeModel = null)
        {
            _boardModel = boardModel;
            _config = config;
            _cellSkinAppliedPublisher = cellSkinAppliedPublisher;
            _gameModeModel = gameModeModel;
            _random = new Random(seed);

            // Subscribing fires immediately with the current score, which is 0 at construction and at
            // every run start (see ResetRun) — never a positive multiple of the interval — so nothing
            // can convert from merely starting to listen.
            _subscription = scoreModel.Score.Subscribe(OnScoreChanged);
        }

        public void Dispose() => _subscription.Dispose();

        /// <summary>
        /// Called by <see cref="BoardSystem.StartNewRun"/> before the opening tray is dealt — never
        /// through <see cref="MustyBlockBlast.Gameplay.Messages.RunStartedMessage"/>, which
        /// <see cref="BoardSystem"/> publishes only after that first refill. Resetting on the message
        /// instead would let the previous run's "unlocked" state theme the very first tray of the next
        /// one (issue #324 AC9).
        /// </summary>
        public void ResetRun()
        {
            _unlockedThemeCount = 0;
            _repeatBatchesTriggered = 0;
        }

        /// <summary>
        /// The skin a freshly drawn tray piece should carry, for <see cref="BoardSystem"/>'s
        /// refill/injection paths to pass into <see cref="TrayModel.SetSlot"/>.
        /// <see cref="CellSkinKind.None"/> outside Classic mode or before this run has unlocked its
        /// first theme; a theme rolled from the currently-unlocked pool otherwise.
        /// </summary>
        internal CellSkinKind DrawSkinForNewPiece()
        {
            if (!SkinsEnabled || _unlockedThemeCount == 0)
            {
                return CellSkinKind.None;
            }

            return RollTheme();
        }

        private bool SkinsEnabled => _gameModeModel == null || _gameModeModel.SkinsEnabled;

        private void OnScoreChanged(int score)
        {
            if (_gameModeModel != null && !_gameModeModel.SkinsEnabled)
            {
                return;
            }

            IReadOnlyList<int> thresholds = _config.ThemeUnlockThresholds;

            // A loop, not a single check: a placement can score enough in one go to cross more than one
            // threshold, and each one earns its own conversion batch.
            while (_unlockedThemeCount < thresholds.Count && score >= thresholds[_unlockedThemeCount])
            {
                _unlockedThemeCount++;
                RunConversionBatch();
            }

            // Only once every theme is unlocked: the repeat cadence themes from the full pool, so there
            // is nothing meaningful for it to trigger before then — the unlock loop above is already
            // this run's only source of batches until its last threshold is crossed.
            if (_unlockedThemeCount < thresholds.Count)
            {
                return;
            }

            int lastThreshold = thresholds[thresholds.Count - 1];
            int interval = _config.RepeatIntervalPoints;
            int repeatBatchesEarned = score >= lastThreshold ? (score - lastThreshold) / interval : 0;

            while (_repeatBatchesTriggered < repeatBatchesEarned)
            {
                _repeatBatchesTriggered++;
                RunConversionBatch();
            }
        }

        /// <summary>
        /// Converts up to <see cref="CellSkinConfig.MaxCellsPerConversion"/> currently-occupied,
        /// still-plain cells to an independently rolled theme each. Reservoir-sampled in a single board
        /// scan so every eligible cell has an equal chance regardless of how many exist, without
        /// collecting them into a throwaway list first. Silently does nothing if no eligible cell exists
        /// — an ordinary outcome (e.g. a near-empty board, or one every cell of which is already
        /// skinned), not an error state.
        /// </summary>
        private void RunConversionBatch()
        {
            Board board = _boardModel.Board;
            int maxCells = _config.MaxCellsPerConversion;
            var reservoir = new GridPosition[maxCells];
            int seenCount = 0;

            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    if (!board.IsOccupied(position) || board.GetCellSkin(position) != CellSkinKind.None)
                    {
                        continue;
                    }

                    if (seenCount < maxCells)
                    {
                        reservoir[seenCount] = position;
                    }
                    else
                    {
                        int replaceIndex = _random.Next(seenCount + 1);
                        if (replaceIndex < maxCells)
                        {
                            reservoir[replaceIndex] = position;
                        }
                    }

                    seenCount++;
                }
            }

            int convertedCount = Math.Min(seenCount, maxCells);
            for (int i = 0; i < convertedCount; i++)
            {
                CellSkinKind theme = RollTheme();
                _boardModel.SetCellSkin(reservoir[i], theme);

                if (_cellSkinAppliedPublisher != null)
                {
                    _cellSkinAppliedPublisher.Publish(new CellSkinAppliedMessage(theme, reservoir[i]));
                }
            }
        }

        /// <summary>One of the currently-unlocked themes, drawn uniformly at random from the first
        /// <see cref="_unlockedThemeCount"/> <see cref="CellSkinKind"/> values (in declaration order) and
        /// fully independently of any other roll — never <see cref="CellSkinKind.None"/>. Callers must
        /// not invoke this while <see cref="_unlockedThemeCount"/> is 0 — both call sites already guard
        /// on it (<see cref="DrawSkinForNewPiece"/> explicitly, <see cref="RunConversionBatch"/>'s only
        /// caller by construction, since it never runs before the first unlock).</summary>
        private CellSkinKind RollTheme() => (CellSkinKind)(_random.Next(_unlockedThemeCount) + 1);
    }
}
