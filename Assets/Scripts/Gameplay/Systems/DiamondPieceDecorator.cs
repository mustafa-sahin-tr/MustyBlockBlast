using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Decides, at draw time, which cells of a freshly dealt piece carry a diamond and of what colour
    /// (issue #394 AC2/AC4). <see cref="BoardSystem"/> asks it once per dealt slot and stores the answer
    /// on <see cref="TrayModel"/>; nothing here touches the tray or the board.
    /// <para>
    /// <b>Gate.</b> Decoration happens only in <see cref="GameMode.Path"/> and only while the run
    /// tracks at least one <see cref="ObjectiveType.DiamondsCleared"/> objective. Both are read live
    /// from their Models on every draw rather than cached at run start: the tracked set is what
    /// <c>LevelProgressionSystem</c> applied for the level being played (it does so before the run is
    /// opened, so the opening deal already sees it), and it is the one authority on "is there an active
    /// diamond objective". Endless and Timed pieces, and Path levels asking for anything else, are never
    /// decorated — every draw there is exactly what it was before this class existed.
    /// </para>
    /// <para>
    /// <b>Colour.</b> A gem is one of the colours the active diamond objectives actually name, chosen
    /// uniformly among them. A level asking for "10 red" therefore only ever deals red gems; a level
    /// asking for red, blue and green deals each about a third of the time.
    /// </para>
    /// <para>
    /// <b>Rate and count (issue #396).</b> How often a piece is decorated, and how many of its cells,
    /// are the level's to tune: <see cref="LevelObjectiveConfig.DiamondDecorationChance"/>,
    /// <see cref="LevelObjectiveConfig.DiamondMinDecoratedCells"/> and
    /// <see cref="LevelObjectiveConfig.DiamondMaxDecoratedCells"/>, read from the active Path level's
    /// first row on every draw (via <see cref="LevelCatalog.Find"/>, the lookup for anything that
    /// belongs to the level rather than to one of its objectives). A level that authors nothing, and a
    /// draw with no active level row to read at all, fall back to the field defaults — the ~20% and
    /// <c>1..CellCount-1</c> of #390 — so nothing dealt before the fields existed deals any differently.
    /// The count is always clamped to <c>1..CellCount-1</c> whatever the level says: a 1-cell piece is
    /// never decorated and no piece ever has every cell decorated.
    /// </para>
    /// <para>
    /// <b>Randomness.</b> Its own seeded stream, exactly as <see cref="WeightedPieceDraw"/> owns its
    /// own: the decoration roll must not perturb the piece and colour sequence a given seed produces,
    /// so a run replays identically with and without the mechanic switched on.
    /// </para>
    /// </summary>
    public sealed class DiamondPieceDecorator
    {
        private readonly ObjectiveModel _objectiveModel;
        private readonly GameModeModel _gameModeModel;
        private readonly LevelCatalog _levelCatalog;
        private readonly PathRunModel _pathRunModel;
        private readonly Random _random;

        /// <summary>The distinct colour ids the active diamond objectives name, rebuilt per decorated
        /// draw. Sized to the palette, so it can never overflow and never reallocates.</summary>
        private readonly int[] _colourPool = new int[Board.COLOUR_COUNT + Collectibles.FRUIT_COUNT];

        /// <summary>Offset indices of the piece being decorated, partially shuffled to pick the
        /// decorated subset without replacement. Grown to the largest piece seen and reused.</summary>
        private int[] _offsetIndexBuffer = Array.Empty<int>();

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        [Inject]
        public DiamondPieceDecorator(
            ObjectiveModel objectiveModel,
            GameModeModel gameModeModel,
            LevelCatalog levelCatalog,
            PathRunModel pathRunModel)
            : this(objectiveModel, gameModeModel, Environment.TickCount, levelCatalog, pathRunModel)
        {
        }

        /// <summary>
        /// Seeded constructor for tests. <paramref name="levelCatalog"/> and <paramref name="pathRunModel"/>
        /// are optional so a test that only exercises the gate and the default roll need not build a
        /// catalog: without both, every draw uses the field defaults, exactly as a run with no active
        /// level row does.
        /// </summary>
        internal DiamondPieceDecorator(
            ObjectiveModel objectiveModel,
            GameModeModel gameModeModel,
            int seed,
            LevelCatalog levelCatalog = null,
            PathRunModel pathRunModel = null)
        {
            _objectiveModel = objectiveModel ?? throw new ArgumentNullException(nameof(objectiveModel));
            _gameModeModel = gameModeModel ?? throw new ArgumentNullException(nameof(gameModeModel));
            _levelCatalog = levelCatalog;
            _pathRunModel = pathRunModel;
            _random = new Random(seed);
        }

        /// <summary>
        /// Whether pieces dealt right now may be decorated at all: a Path run tracking at least one
        /// <see cref="ObjectiveType.DiamondsCleared"/> objective (AC4).
        /// </summary>
        internal bool IsActive
        {
            get
            {
                if (_gameModeModel.CurrentMode.Value != GameMode.Path)
                {
                    return false;
                }

                return CollectColourPool() > 0;
            }
        }

        /// <summary>
        /// Rolls the decoration for <paramref name="piece"/>. Returns true when the piece came out
        /// decorated, with <paramref name="diamondColourIdsByOffset"/> holding a gem colour for each
        /// decorated <see cref="Piece.Offsets"/> index and <see cref="TrayModel.NO_DIAMOND"/> everywhere
        /// else; returns false — buffer zeroed — for a plain draw. A 1-cell piece is never decorated and
        /// a multi-cell piece never has every cell decorated: the count is uniform in the level's
        /// authored range, clamped to <c>1..CellCount-1</c> (AC2).
        /// <para>
        /// The buffer must be at least <see cref="Piece.CellCount"/> long; it is owned by the caller and
        /// reused, so a decorated draw allocates nothing.
        /// </para>
        /// </summary>
        internal bool TryDecorate(Piece piece, int[] diamondColourIdsByOffset)
        {
            if (piece == null)
            {
                throw new ArgumentNullException(nameof(piece));
            }

            if (diamondColourIdsByOffset == null || diamondColourIdsByOffset.Length < piece.CellCount)
            {
                throw new ArgumentException(
                    "The decoration buffer must hold one entry per piece cell.", nameof(diamondColourIdsByOffset));
            }

            Array.Clear(diamondColourIdsByOffset, 0, diamondColourIdsByOffset.Length);

            int cellCount = piece.CellCount;
            if (cellCount <= 1 || !IsActive)
            {
                return false;
            }

            LevelObjectiveConfig level = ActiveLevelConfig();

            // NextDouble is in [0, 1), so a chance of 0 never decorates and a chance of 1 always does.
            double chance = level != null
                ? level.DiamondDecorationChance
                : LevelObjectiveConfig.DEFAULT_DIAMOND_DECORATION_CHANCE;
            if (_random.NextDouble() >= chance)
            {
                return false;
            }

            // IsActive above has just filled the pool; it is non-empty or we would not be here.
            int poolCount = CollectColourPool();
            ResolveDecoratedCountRange(level, cellCount, out int minCount, out int maxCount);
            int decoratedCount = _random.Next(minCount, maxCount + 1);

            EnsureOffsetIndexBuffer(cellCount);
            for (int offsetIndex = 0; offsetIndex < cellCount; offsetIndex++)
            {
                _offsetIndexBuffer[offsetIndex] = offsetIndex;
            }

            // Partial Fisher-Yates: the first decoratedCount entries end up a uniform subset without
            // replacement, and nothing past them is ever read.
            for (int pickIndex = 0; pickIndex < decoratedCount; pickIndex++)
            {
                int swapIndex = pickIndex + _random.Next(cellCount - pickIndex);
                int picked = _offsetIndexBuffer[swapIndex];
                _offsetIndexBuffer[swapIndex] = _offsetIndexBuffer[pickIndex];
                _offsetIndexBuffer[pickIndex] = picked;

                diamondColourIdsByOffset[picked] = _colourPool[_random.Next(poolCount)];
            }

            return true;
        }

        /// <summary>
        /// The row whose diamond tunables this draw reads: the active Path level's first row, or null
        /// when there is none to read — no catalog or run model injected (tests), no active Path level
        /// (<see cref="PathRunModel.NO_ACTIVE_LEVEL"/>), or a level number the catalog does not author.
        /// </summary>
        private LevelObjectiveConfig ActiveLevelConfig()
        {
            if (_levelCatalog == null || _pathRunModel == null)
            {
                return null;
            }

            int activeLevelNumber = _pathRunModel.ActiveLevelNumber.Value;
            if (activeLevelNumber == PathRunModel.NO_ACTIVE_LEVEL)
            {
                return null;
            }

            return _levelCatalog.Find(activeLevelNumber);
        }

        /// <summary>
        /// The inclusive range the decorated count is drawn from for a <paramref name="cellCount"/>-cell
        /// piece. Without a level row it is the fixed <c>1..CellCount-1</c> of #390. With one, the
        /// authored min and max are clamped into that same band — the band is a rule of the mechanic,
        /// not a default the level may override — and an uncapped max means the band's top.
        /// </summary>
        private static void ResolveDecoratedCountRange(
            LevelObjectiveConfig level, int cellCount, out int minCount, out int maxCount)
        {
            int hardMax = cellCount - 1;
            minCount = LevelObjectiveConfig.DEFAULT_DIAMOND_MIN_DECORATED_CELLS;
            maxCount = hardMax;

            if (level == null)
            {
                return;
            }

            minCount = Clamp(level.DiamondMinDecoratedCells, LevelObjectiveConfig.DEFAULT_DIAMOND_MIN_DECORATED_CELLS, hardMax);

            int authoredMax = level.DiamondMaxDecoratedCells;
            if (authoredMax != LevelObjectiveConfig.DIAMOND_MAX_DECORATED_CELLS_UNCAPPED)
            {
                maxCount = Clamp(authoredMax, minCount, hardMax);
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        /// <summary>Fills <see cref="_colourPool"/> with the distinct colour ids the tracked, still
        /// incomplete <see cref="ObjectiveType.DiamondsCleared"/> objectives name and returns how many
        /// there are; 0 when no such objective is tracked. A completed one is left out (issue #429):
        /// once its colour's target is met, dealing more of it only wastes decorated cells the player
        /// can no longer use, on levels that still track other colours.</summary>
        private int CollectColourPool()
        {
            int poolCount = 0;
            IReadOnlyList<ObjectiveProgress> tracked = _objectiveModel.TrackedObjectives;

            for (int objectiveIndex = 0; objectiveIndex < tracked.Count; objectiveIndex++)
            {
                ObjectiveProgress objective = tracked[objectiveIndex];
                ObjectiveDefinition definition = objective.Definition;
                // A fruit objective (issue #484) feeds the same pool: its RequiredColourId is the fruit's
                // collectible id, so pieces carry fruits exactly as they carry diamonds.
                bool isCollectibleObjective = definition.Type == ObjectiveType.DiamondsCleared
                    || definition.Type == ObjectiveType.FruitsCollected;
                if (!isCollectibleObjective || objective.IsComplete)
                {
                    continue;
                }

                int colourId = definition.RequiredColourId;
                if (!Collectibles.IsValid(colourId) || Contains(_colourPool, poolCount, colourId))
                {
                    continue;
                }

                _colourPool[poolCount] = colourId;
                poolCount++;
            }

            return poolCount;
        }

        private static bool Contains(int[] values, int count, int value)
        {
            for (int valueIndex = 0; valueIndex < count; valueIndex++)
            {
                if (values[valueIndex] == value)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureOffsetIndexBuffer(int cellCount)
        {
            if (_offsetIndexBuffer.Length < cellCount)
            {
                _offsetIndexBuffer = new int[cellCount];
            }
        }
    }
}
