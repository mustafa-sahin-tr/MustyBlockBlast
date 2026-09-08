using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The full, fixed set of piece definitions (see docs/game-design.md, "Pieces"). Pieces are
    /// never rotated at runtime — every orientation offered by the design is its own entry here.
    /// </summary>
    public static class PieceCatalog
    {
        public static IReadOnlyList<Piece> AllPieces { get; } = BuildAllPieces();

        private static Piece[] BuildAllPieces()
        {
            var pieces = new List<Piece>();
            pieces.Add(Single());
            pieces.AddRange(Lines());
            pieces.AddRange(Squares());
            pieces.AddRange(Corners2X2());
            pieces.AddRange(Corners3X3());
            pieces.AddRange(Tetrominoes());
            return pieces.ToArray();
        }

        private static Piece Single()
            => new Piece("single_1x1", new[] { new GridPosition(0, 0) });

        private static IEnumerable<Piece> Lines()
        {
            for (int length = 2; length <= 5; length++)
            {
                yield return new Piece($"line_h{length}", HorizontalRun(length));
                yield return new Piece($"line_v{length}", VerticalRun(length));
            }
        }

        private static IEnumerable<Piece> Squares()
        {
            yield return new Piece("square_2x2", Rectangle(2, 2));
            yield return new Piece("square_3x3", Rectangle(3, 3));
        }

        /// <summary>3-cell L-tromino: a 2x2 square with one corner missing, one piece per missing corner.</summary>
        private static IEnumerable<Piece> Corners2X2()
        {
            yield return new Piece("corner2_missing_tr", new[]
            {
                new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(0, 1),
            });
            yield return new Piece("corner2_missing_tl", new[]
            {
                new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(1, 1),
            });
            yield return new Piece("corner2_missing_bl", new[]
            {
                new GridPosition(1, 0), new GridPosition(0, 1), new GridPosition(1, 1),
            });
            yield return new Piece("corner2_missing_br", new[]
            {
                new GridPosition(0, 0), new GridPosition(0, 1), new GridPosition(1, 1),
            });
        }

        /// <summary>5-cell L-shape: two 3-long arms of a 3x3 square sharing a corner cell.</summary>
        private static IEnumerable<Piece> Corners3X3()
        {
            yield return new Piece("corner3_bl", new[]
            {
                new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(2, 0),
                new GridPosition(0, 1), new GridPosition(0, 2),
            });
            yield return new Piece("corner3_br", new[]
            {
                new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(2, 0),
                new GridPosition(2, 1), new GridPosition(2, 2),
            });
            yield return new Piece("corner3_tl", new[]
            {
                new GridPosition(0, 0), new GridPosition(0, 1), new GridPosition(0, 2),
                new GridPosition(1, 2), new GridPosition(2, 2),
            });
            yield return new Piece("corner3_tr", new[]
            {
                new GridPosition(2, 0), new GridPosition(2, 1), new GridPosition(2, 2),
                new GridPosition(1, 2), new GridPosition(0, 2),
            });
        }

        /// <summary>4-cell T, S and Z tetrominoes. S and Z have 2 distinct orientations each
        /// (180-degree rotation reproduces the same offset set); T has 4.</summary>
        private static IEnumerable<Piece> Tetrominoes()
        {
            yield return new Piece("t_up", new[]
            {
                new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(2, 0), new GridPosition(1, 1),
            });
            yield return new Piece("t_down", new[]
            {
                new GridPosition(0, 1), new GridPosition(1, 1), new GridPosition(2, 1), new GridPosition(1, 0),
            });
            yield return new Piece("t_right", new[]
            {
                new GridPosition(0, 0), new GridPosition(0, 1), new GridPosition(0, 2), new GridPosition(1, 1),
            });
            yield return new Piece("t_left", new[]
            {
                new GridPosition(1, 0), new GridPosition(1, 1), new GridPosition(1, 2), new GridPosition(0, 1),
            });

            yield return new Piece("s_horizontal", new[]
            {
                new GridPosition(0, 1), new GridPosition(1, 1), new GridPosition(1, 0), new GridPosition(2, 0),
            });
            yield return new Piece("s_vertical", new[]
            {
                new GridPosition(0, 1), new GridPosition(0, 2), new GridPosition(1, 0), new GridPosition(1, 1),
            });

            yield return new Piece("z_horizontal", new[]
            {
                new GridPosition(0, 0), new GridPosition(1, 0), new GridPosition(1, 1), new GridPosition(2, 1),
            });
            yield return new Piece("z_vertical", new[]
            {
                new GridPosition(1, 1), new GridPosition(1, 2), new GridPosition(0, 0), new GridPosition(0, 1),
            });
        }

        private static GridPosition[] HorizontalRun(int length)
        {
            var offsets = new GridPosition[length];
            for (int i = 0; i < length; i++)
            {
                offsets[i] = new GridPosition(i, 0);
            }

            return offsets;
        }

        private static GridPosition[] VerticalRun(int length)
        {
            var offsets = new GridPosition[length];
            for (int i = 0; i < length; i++)
            {
                offsets[i] = new GridPosition(0, i);
            }

            return offsets;
        }

        private static GridPosition[] Rectangle(int width, int height)
        {
            var offsets = new GridPosition[width * height];
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    offsets[index] = new GridPosition(x, y);
                    index++;
                }
            }

            return offsets;
        }
    }
}
