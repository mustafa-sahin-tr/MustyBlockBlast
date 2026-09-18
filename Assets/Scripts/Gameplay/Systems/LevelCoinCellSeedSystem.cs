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
    /// Puts the <see cref="SpecialCellKind.Coin"/> cells a level authors
    /// (<see cref="LevelObjectiveConfig.CoinCellCount"/>) onto that level's board.
    /// <para>
    /// <b>Why this is not done at run start.</b> A run starts on an empty board —
    /// <see cref="BoardSystem.StartNewRun"/> calls <c>ClearAll</c> and then refills the dock, and
    /// <c>RunStartedMessage</c> is published after that refill but before anything has been placed. A
    /// coin cell, like every other special kind in this game, is a <em>property of a block</em>: it is
    /// painted onto a cell that already holds one and never adds a block of its own (see
    /// <see cref="CoinSpawnSelector"/>, and <see cref="LaserSpawnSystem"/>/
    /// <c>GoldenPieceTriggerSystem</c>, which work the same way). So at the one moment a "level start"
    /// hook could fire there is, by construction, nothing on the board to paint.
    /// </para>
    /// <para>
    /// The debt is therefore <em>armed</em> at run start and <em>paid</em> at the first placement that
    /// leaves a block standing — which from the player's point of view is still the opening of the
    /// level, and is the earliest moment the authored count can mean anything at all. If that first
    /// placement happened to empty the board again the debt simply stays owed and the next placement
    /// pays it; nothing is dropped and nothing is paid twice.
    /// </para>
    /// <para>
    /// Its own System rather than a branch of <see cref="LevelProgressionSystem"/>, which is the
    /// obvious candidate: that System owns which level is being played and what happens when it is
    /// cleared, and it deliberately touches the board through nothing but
    /// <see cref="BoardSystem.StartNewRun"/>/<see cref="BoardSystem.ForceGameOver"/>. Seeding cells is a
    /// board write, and giving it to the progression system would be the first of those. Note also that
    /// <see cref="LevelObjectiveConfig.ToBoardShape"/> is still unreferenced anywhere in the project, so
    /// there is no existing "apply this level's board setup" seam to hang this off — when one arrives,
    /// this System is what should move into it.
    /// </para>
    /// <para>
    /// Which level is read the same way the rest of the game reads it: the Path run's active level when
    /// there is one, otherwise the linear frontier. A level the catalog does not author, or one
    /// authoring zero, seeds nothing.
    /// </para>
    /// <para>
    /// <b>The second source.</b> A level's coin cells are not all authored: the Coin Sower power-up lets
    /// the player buy extra ones at the level-start screen, and those arrive through
    /// <see cref="QueueExtraCoinCells"/> — called by that screen immediately before it asks
    /// <see cref="LevelProgressionSystem.TryStartPathLevel"/> to open the run. The queued quantity is
    /// <em>added</em> to the authored count by the next <c>RunStartedMessage</c> and cleared as it is
    /// consumed, so it is sown exactly once: the run it was bought for, never the one after it.
    /// </para>
    /// <para>
    /// Folded into this System rather than given one of its own on purpose. A second subscriber to the
    /// same two messages would paint cells concurrently with this one, with neither holding the other's
    /// list of cells to avoid, so the bounded re-rolls below would fight each other and a purchase of
    /// three could visibly land as two. One owner of "coin cells owed this run" — whatever owes them —
    /// is what makes the distinctness attempt mean anything.
    /// </para>
    /// </summary>
    public sealed class LevelCoinCellSeedSystem : IDisposable
    {
        /// <summary>
        /// How many cells one owed coin will try before it accepts overwriting a cell that is already a
        /// coin. The selector picks uniformly among occupied cells and knows nothing about what is
        /// already on them, so on a nearly-empty board two owed coins can land on the same cell; a few
        /// re-rolls make a level authoring three coins actually show three of them without the
        /// selector having to grow a concept of "cells to avoid" that its other two callers do not want.
        /// Bounded rather than a loop-until-distinct, because a board with fewer occupied cells than the
        /// level owes coins has no distinct answer to find.
        /// </summary>
        private const int DISTINCT_CELL_ATTEMPTS = 8;

        private readonly BoardModel _boardModel;
        private readonly LevelCatalog _levelCatalog;
        private readonly LevelProgressionModel _progressionModel;
        private readonly PathRunModel _pathRunModel;
        private readonly Random _random;
        private readonly IDisposable _subscriptions;

        /// <summary>Coin cells this run still owes the board. Armed at run start and drained by the
        /// first placement that leaves something to paint them onto.</summary>
        private int _pendingCoinCells;

        /// <summary>Coin cells bought at the level-start screen and not yet handed to a run. Waits here
        /// only for the moment between the purchase and the <c>RunStartedMessage</c> the same tap causes,
        /// which consumes and clears it.</summary>
        private int _queuedExtraCoinCells;

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        /// <summary>Optional: null in every existing test construction, which predates issue #278.
        /// Guarded on publish so an un-injected instance behaves exactly as it did before.</summary>
        private readonly IPublisher<SpecialCellSpawnedMessage> _specialCellSpawnedPublisher;

        [Inject]
        public LevelCoinCellSeedSystem(
            BoardModel boardModel,
            LevelCatalog levelCatalog,
            LevelProgressionModel progressionModel,
            PathRunModel pathRunModel,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<PiecePlacedMessage> piecePlacedSubscriber,
            IPublisher<SpecialCellSpawnedMessage> specialCellSpawnedPublisher = null)
            : this(
                boardModel, levelCatalog, progressionModel, pathRunModel, runStartedSubscriber,
                piecePlacedSubscriber, Environment.TickCount, specialCellSpawnedPublisher)
        {
        }

        internal LevelCoinCellSeedSystem(
            BoardModel boardModel,
            LevelCatalog levelCatalog,
            LevelProgressionModel progressionModel,
            PathRunModel pathRunModel,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<PiecePlacedMessage> piecePlacedSubscriber,
            int seed,
            IPublisher<SpecialCellSpawnedMessage> specialCellSpawnedPublisher = null)
        {
            _boardModel = boardModel;
            _levelCatalog = levelCatalog;
            _progressionModel = progressionModel;
            _pathRunModel = pathRunModel;
            _random = new Random(seed);
            _specialCellSpawnedPublisher = specialCellSpawnedPublisher;

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            runStartedSubscriber.Subscribe(OnRunStarted).AddTo(bag);
            piecePlacedSubscriber.Subscribe(OnPiecePlaced).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        /// <summary>
        /// Adds <paramref name="count"/> coin cells to whatever the next run starts owing, on top of the
        /// level's own authored count. The Coin Sower power-up's whole board-side effect: the level-start
        /// screen calls this for the quantity it has just bought and spent, then starts the level.
        /// <para>
        /// Replaces rather than accumulates, and is cleared by the run that consumes it, so a purchase
        /// dresses exactly one run. The two halves of that are deliberately split: a second call before
        /// any run starts is the same screen having been reopened and re-committed, which must offer the
        /// new quantity and not the sum of both attempts.
        /// </para>
        /// <para>
        /// A negative count is floored rather than trusted, for the reason the authored count is: it
        /// could only ever subtract from the cells the level itself asked for.
        /// </para>
        /// </summary>
        public void QueueExtraCoinCells(int count)
        {
            _queuedExtraCoinCells = Math.Max(0, count);
        }

        /// <summary>
        /// Arms this run's debt from both sources — what the level authors and what the player bought —
        /// overwriting rather than adding to whatever the previous run left owed: a coin cell is authored
        /// per level, so a run that ended before its cells were ever painted must not hand them to the
        /// next one.
        /// <para>
        /// The purchased quantity is cleared as it is taken up, which is what makes it a one-run debt:
        /// the player paid for this level's cells, not for every level they go on to play.
        /// </para>
        /// </summary>
        private void OnRunStarted(RunStartedMessage message)
        {
            LevelObjectiveConfig level = _levelCatalog.Find(CurrentLevelNumber());
            int authoredCount = level != null ? Math.Max(0, level.CoinCellCount) : 0;

            _pendingCoinCells = authoredCount + _queuedExtraCoinCells;
            _queuedExtraCoinCells = 0;
        }

        /// <summary>
        /// Pays as much of the debt as the board can hold blocks for. Runs on every placement but does
        /// nothing at all once the debt is clear, which is every placement after the first.
        /// </summary>
        private void OnPiecePlaced(PiecePlacedMessage message)
        {
            while (_pendingCoinCells > 0)
            {
                GridPosition? spawn = SelectDistinctSpawn();
                if (spawn == null)
                {
                    // Nothing on the board to paint — a placement that completed a line and emptied it.
                    // The debt stays owed for the next placement rather than being dropped.
                    return;
                }

                _boardModel.SetSpecialKind(spawn.Value, SpecialCellKind.Coin);

                if (_specialCellSpawnedPublisher != null)
                {
                    _specialCellSpawnedPublisher.Publish(
                        new SpecialCellSpawnedMessage(SpecialCellKind.Coin, spawn.Value));
                }

                _pendingCoinCells--;
            }
        }

        /// <summary>A spawn cell that is not already a coin, or — when a bounded number of picks all
        /// land on one — whichever the last pick was. Null only when the board holds no block at all,
        /// which is the selector's own answer and not a failure state.</summary>
        private GridPosition? SelectDistinctSpawn()
        {
            GridPosition? spawn = null;

            for (int attempt = 0; attempt < DISTINCT_CELL_ATTEMPTS; attempt++)
            {
                spawn = CoinSpawnSelector.SelectSpawnPosition(_boardModel.Board, _random);
                if (spawn == null || _boardModel.GetSpecialKind(spawn.Value) != SpecialCellKind.Coin)
                {
                    return spawn;
                }
            }

            return spawn;
        }

        /// <summary>The level the run in progress is playing: the Path run's active level when there is
        /// one, otherwise the linear frontier. The same distinction
        /// <see cref="LevelProgressionSystem"/> keeps, read rather than duplicated as state here.</summary>
        private int CurrentLevelNumber()
        {
            int activeLevel = _pathRunModel.ActiveLevelNumber.Value;
            return activeLevel != PathRunModel.NO_ACTIVE_LEVEL
                ? activeLevel
                : _progressionModel.CurrentLevelNumber.Value;
        }
    }
}
