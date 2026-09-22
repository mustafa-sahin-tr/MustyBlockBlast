using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
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
    /// asking for red, blue and green deals each about a third of the time. Level-authored spawn rate
    /// and colour weighting are the next slice (#396); until then the chance and the colour rule are
    /// the fixed defaults below.
    /// </para>
    /// <para>
    /// <b>Randomness.</b> Its own seeded stream, exactly as <see cref="WeightedPieceDraw"/> owns its
    /// own: the decoration roll must not perturb the piece and colour sequence a given seed produces,
    /// so a run replays identically with and without the mechanic switched on.
    /// </para>
    /// </summary>
    public sealed class DiamondPieceDecorator
    {
        /// <summary>Share of eligible (multi-cell) draws that come out decorated. The "~20%" of #390.</summary>
        internal const double DECORATION_CHANCE = 0.2;

        private readonly ObjectiveModel _objectiveModel;
        private readonly GameModeModel _gameModeModel;
        private readonly Random _random;

        /// <summary>The distinct colour ids the active diamond objectives name, rebuilt per decorated
        /// draw. Sized to the palette, so it can never overflow and never reallocates.</summary>
        private readonly int[] _colourPool = new int[Board.COLOUR_COUNT];

        /// <summary>Offset indices of the piece being decorated, partially shuffled to pick the
        /// decorated subset without replacement. Grown to the largest piece seen and reused.</summary>
        private int[] _offsetIndexBuffer = Array.Empty<int>();

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        [Inject]
        public DiamondPieceDecorator(ObjectiveModel objectiveModel, GameModeModel gameModeModel)
            : this(objectiveModel, gameModeModel, Environment.TickCount)
        {
        }

        internal DiamondPieceDecorator(ObjectiveModel objectiveModel, GameModeModel gameModeModel, int seed)
        {
            _objectiveModel = objectiveModel ?? throw new ArgumentNullException(nameof(objectiveModel));
            _gameModeModel = gameModeModel ?? throw new ArgumentNullException(nameof(gameModeModel));
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
        /// a multi-cell piece never has every cell decorated: the count is uniform in
        /// <c>1..CellCount-1</c> (AC2).
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

            if (_random.NextDouble() >= DECORATION_CHANCE)
            {
                return false;
            }

            // IsActive above has just filled the pool; it is non-empty or we would not be here.
            int poolCount = CollectColourPool();
            int decoratedCount = _random.Next(1, cellCount);

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

        /// <summary>Fills <see cref="_colourPool"/> with the distinct colour ids the tracked
        /// <see cref="ObjectiveType.DiamondsCleared"/> objectives name and returns how many there are;
        /// 0 when no such objective is tracked.</summary>
        private int CollectColourPool()
        {
            int poolCount = 0;
            IReadOnlyList<ObjectiveProgress> tracked = _objectiveModel.TrackedObjectives;

            for (int objectiveIndex = 0; objectiveIndex < tracked.Count; objectiveIndex++)
            {
                ObjectiveDefinition definition = tracked[objectiveIndex].Definition;
                if (definition.Type != ObjectiveType.DiamondsCleared)
                {
                    continue;
                }

                int colourId = definition.RequiredColourId;
                if (colourId < 1 || colourId > Board.COLOUR_COUNT || Contains(_colourPool, poolCount, colourId))
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
