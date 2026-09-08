using System;
using MustyBlockBlast.Core;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Weighted draw from <see cref="PieceCatalog"/>: large pieces are rarer than small ones
    /// (docs/game-design.md, "Drawing pieces"). Solvability is deliberately not guaranteed.
    /// </summary>
    public sealed class WeightedPieceDraw
    {
        /// <summary>Number of distinct cosmetic colour ids ("piece kinds"). Colour never affects rules.</summary>
        public const int COLOUR_COUNT = 3;

        private readonly Random _random;
        private readonly int[] _weights;
        private readonly int _totalWeight;

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        [Inject]
        public WeightedPieceDraw()
            : this(Environment.TickCount)
        {
        }

        public WeightedPieceDraw(int seed)
        {
            _random = new Random(seed);
            _weights = new int[PieceCatalog.AllPieces.Count];

            int total = 0;
            for (int i = 0; i < PieceCatalog.AllPieces.Count; i++)
            {
                int weight = WeightFor(PieceCatalog.AllPieces[i].CellCount);
                _weights[i] = weight;
                total += weight;
            }

            _totalWeight = total;
        }

        public Piece DrawPiece()
        {
            int roll = _random.Next(_totalWeight);
            for (int i = 0; i < _weights.Length; i++)
            {
                roll -= _weights[i];
                if (roll < 0)
                {
                    return PieceCatalog.AllPieces[i];
                }
            }

            return PieceCatalog.AllPieces[PieceCatalog.AllPieces.Count - 1];
        }

        public int DrawColourId() => _random.Next(1, COLOUR_COUNT + 1);

        private static int WeightFor(int cellCount)
        {
            if (cellCount <= 2)
            {
                return 10;
            }

            if (cellCount == 3)
            {
                return 8;
            }

            if (cellCount == 4)
            {
                return 6;
            }

            if (cellCount == 5)
            {
                return 3;
            }

            return 1;
        }
    }
}
