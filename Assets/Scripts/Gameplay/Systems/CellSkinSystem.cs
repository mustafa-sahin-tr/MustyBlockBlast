using System;
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
    /// (<see cref="GameMode.Timed"/>) run's score repeatedly crossing a configured interval
    /// (<see cref="CellSkinConfig.ScoreIntervalPoints"/>) converts a few currently-occupied, still-plain
    /// board cells to a randomly themed <see cref="CellSkinKind"/>, and — from that first conversion
    /// onward, for the rest of the run — every newly drawn tray piece also comes pre-themed
    /// (<see cref="DrawSkinForNewPiece"/>, called by <see cref="BoardSystem"/>'s refill/injection paths).
    /// <para>
    /// Its own System rather than another branch of <see cref="BoardSystem"/>, for the reason
    /// <see cref="LaserSpawnSystem"/>/<see cref="CoinStreakTriggerSystem"/> are their own: the trigger
    /// is a score concept layered on top of the board, not a placement-resolution concern.
    /// </para>
    /// <para>
    /// Gated on <see cref="GameModeModel.SkinsEnabled"/> — deliberately not
    /// <see cref="GameModeModel.ExtrasEnabled"/> — everywhere: the score-threshold conversion, the
    /// per-piece draw, all of it. Every mode other than Classic never assigns a skin (issue #324 AC0/AC8).
    /// </para>
    /// <para>
    /// Every theme roll (which of the four skins a converted cell or a drawn piece gets) is fully
    /// independent and uniformly random — no single-theme-per-run, no sequential progression — per the
    /// confirmed design decision.
    /// </para>
    /// </summary>
    public sealed class CellSkinSystem : IDisposable
    {
        private readonly BoardModel _boardModel;
        private readonly GameModeModel _gameModeModel;
        private readonly CellSkinConfig _config;
        private readonly IPublisher<CellSkinAppliedMessage> _cellSkinAppliedPublisher;

        /// <summary>Picks which occupied cells convert and which theme any conversion or draw rolls.
        /// Whether a threshold has been crossed at all is fully deterministic (score-driven) and never
        /// touches this.</summary>
        private readonly Random _random;

        private readonly IDisposable _subscription;

        /// <summary>How many score-interval multiples this run has already crossed. Reset to 0 by
        /// <see cref="ResetRun"/>, never by reading <see cref="ScoreModel.Score"/> — that value is not
        /// guaranteed to already be 0 when a new run's tray is first dealt (see <see cref="ResetRun"/>'s
        /// own remarks).</summary>
        private int _lastThresholdReached;

        /// <summary>True once this run has crossed its first threshold — from then on
        /// <see cref="DrawSkinForNewPiece"/> themes every newly drawn piece. Reset by
        /// <see cref="ResetRun"/>.</summary>
        private bool _unlockedForRun;

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
            _lastThresholdReached = 0;
            _unlockedForRun = false;
        }

        /// <summary>
        /// The skin a freshly drawn tray piece should carry, for <see cref="BoardSystem"/>'s
        /// refill/injection paths to pass into <see cref="TrayModel.SetSlot"/>.
        /// <see cref="CellSkinKind.None"/> outside Classic mode or before this run has crossed its first
        /// threshold; an independently rolled theme otherwise.
        /// </summary>
        internal CellSkinKind DrawSkinForNewPiece()
        {
            if (!SkinsEnabled || !_unlockedForRun)
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

            int interval = _config.ScoreIntervalPoints;
            int currentThreshold = score / interval;

            // A loop, not a single check: a placement can score enough in one go to cross more than one
            // multiple of the interval, and each one earns its own conversion batch.
            while (_lastThresholdReached < currentThreshold)
            {
                _lastThresholdReached++;
                _unlockedForRun = true;
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

        /// <summary>One of the four themes, drawn uniformly at random and fully independently of any
        /// other roll — never <see cref="CellSkinKind.None"/>.</summary>
        private CellSkinKind RollTheme() => (CellSkinKind)(_random.Next(4) + 1);
    }
}
