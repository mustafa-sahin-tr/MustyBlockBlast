using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.ColourCleared"/> objective card's demo (issue #453, mockup artboard
    /// "Renk"), authored from the real rule (<see cref="ObjectiveProgress"/>, <c>DestroyedCountOf</c>): every
    /// destroyed block of the objective's colour counts — cells, not clears — so one clear taking two of them
    /// moves progress on by two. The colour is a colour id (<see cref="ObjectiveDefinition.RequiredColourId"/>,
    /// 1..5), painted in the current theme's own fill for it, so the demo follows both the objective and the
    /// theme; one demo per colour id (<see cref="Build(int)"/>).
    /// <para>
    /// Choreography (5.4 s loop; board rows top to bottom, '.' empty; 'X' the objective's colour, A/B/C/D the
    /// other four colour ids in ascending order):
    /// <code>
    /// row0-2 ........
    /// row3 ...A....
    /// row4 ..C.....
    /// row5 ABX.BACC     row 5 is full except (5,3)
    /// row6 ....A...
    /// row7 XXACBB.C     row 7 is full except (7,6)
    /// </code>
    /// The strip holds the progress chip (a block of the colour, 0/3) on the left and three tray pieces on the
    /// right: a single (D), a single (B) and an L corner (C), none of them the objective's colour.
    /// 0.1 s the three X blocks pulse ("these") · 0.45 s the first single lands on (7,6) · 1.3 s row 7 clears,
    /// taking two X blocks · the chip jumps 0/3 → 2/3 · 2.2 s the second single lands on (5,3) · 3.05 s row 5
    /// clears, taking the last · 3/3 and the green check · 3.5 s "Colour cleared!" in the colour itself · hold,
    /// fade out from 5.0 s, loop.
    /// </para>
    /// </summary>
    internal static class ColourClearedInfoDemo
    {
        internal const float LOOP_DURATION = 5.4f;

        /// <summary>How many blocks of the colour the demo clears — its chip target.</summary>
        internal const int TARGET = 3;

        internal const float PULSE_START = 0.1f;
        internal const float PULSE_DURATION = 0.8f;

        internal const float FIRST_PLACE_START = 0.45f;
        internal const float FIRST_CLEAR_START = 1.3f;
        internal const float SECOND_PLACE_START = 2.2f;
        internal const float SECOND_CLEAR_START = 3.05f;
        internal const float LABEL_START = 3.5f;

        internal const int FIRST_ROW = 7;
        internal const int FIRST_COLUMN = 6;
        internal const int SECOND_ROW = 5;
        internal const int SECOND_COLUMN = 3;

        /// <summary>'X' the objective's colour; 'A'..'D' the other colour ids in ascending order.</summary>
        internal static readonly string[] Template =
        {
            "........",
            "........",
            "........",
            "...A....",
            "..C.....",
            "ABX.BACC",
            "....A...",
            "XXACBB.C",
        };

        internal static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };

        /// <summary>True when <paramref name="colourId"/> is a block colour id the demo can draw.</summary>
        internal static bool Supports(int colourId) => colourId >= 1 && colourId <= Board.COLOUR_COUNT;

        /// <summary>The paint of template letter <paramref name="letter"/> for objective colour
        /// <paramref name="colourId"/>: 'X' the colour itself, 'A'..'D' the other ids in ascending order.</summary>
        internal static int PaintFor(char letter, int colourId)
        {
            if (letter == 'X')
            {
                return colourId;
            }

            int otherIndex = letter - 'A';
            int found = -1;
            for (int candidate = 1; candidate <= Board.COLOUR_COUNT; candidate++)
            {
                if (candidate == colourId)
                {
                    continue;
                }

                found++;
                if (found == otherIndex)
                {
                    return candidate;
                }
            }

            return InfoDemoPaint.NONE;
        }

        /// <summary>The starting board for <paramref name="colourId"/>, one paint per cell, [row, column].</summary>
        internal static int[,] StartPaints(int colourId)
        {
            int[,] paints = new int[InfoDemoLayout.BOARD_SIZE, InfoDemoLayout.BOARD_SIZE];
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    char letter = Template[row][column];
                    paints[row, column] = letter == '.' ? InfoDemoPaint.NONE : PaintFor(letter, colourId);
                }
            }

            return paints;
        }

        internal static InfoDemoTimeline Build(int colourId) => Build(colourId, out _);

        /// <summary>As <see cref="Build(int)"/>, handing back the chip so a test can read it.</summary>
        internal static InfoDemoTimeline Build(int colourId, out InfoDemoProgressChip chip)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            int[,] paints = StartPaints(colourId);
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    builder.SetBoardCell(row, column, paints[row, column]);
                }
            }

            // The chip's glyph is a block of the colour — a swatch in the board's own block look.
            chip = InfoDemoChoreography.ProgressChip(builder, SingleShape, colourId, 0, TARGET);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            int firstPaint = PaintFor('D', colourId);
            int secondPaint = PaintFor('B', colourId);
            Vector2 firstSlot = InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft);
            Vector2 secondSlot = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            int first = builder.AddPiece(SingleShape, firstPaint, firstSlot, scale);
            int second = builder.AddPiece(SingleShape, secondPaint, secondSlot, scale);
            builder.AddPiece(CornerShape, PaintFor('C', colourId), InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            // 0. "These": every block of the colour pulses.
            InfoDemoChoreography.PulseCells(builder, CellsOfColour(colourId), PULSE_START, PULSE_DURATION, 0.5f, 2);

            // 1. Row 7 clears two of them at once — the chip counts both.
            InfoDemoChoreography.PlacePiece(builder, first, SingleShape, firstPaint, firstSlot, FIRST_ROW, FIRST_COLUMN, FIRST_PLACE_START);
            InfoDemoChoreography.ClearRow(builder, FIRST_ROW, FIRST_CLEAR_START);
            InfoDemoChoreography.AdvanceChip(builder, chip, ColourGoneTime(FIRST_CLEAR_START, 1), CountInRow(paints, FIRST_ROW, colourId));

            // 2. Row 5 clears the last one.
            InfoDemoChoreography.PlacePiece(
                builder, second, SingleShape, secondPaint, secondSlot, SECOND_ROW, SECOND_COLUMN, SECOND_PLACE_START);
            InfoDemoChoreography.ClearRow(builder, SECOND_ROW, SECOND_CLEAR_START);
            InfoDemoChoreography.AdvanceChip(builder, chip, ColourGoneTime(SECOND_CLEAR_START, 2), CountInRow(paints, SECOND_ROW, colourId));

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_COLOUR_GONE, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 4.6f),
                1.0f, colourId, LABEL_START, 1.3f);

            return builder.Build();
        }

        /// <summary>The cells (x = column, y = row) holding the objective's colour at loop start.</summary>
        internal static Vector2Int[] CellsOfColour(int colourId)
        {
            int[,] paints = StartPaints(colourId);
            int count = 0;
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    if (paints[row, column] == colourId)
                    {
                        count++;
                    }
                }
            }

            Vector2Int[] cells = new Vector2Int[count];
            int cellIndex = 0;
            for (int row = 0; row < InfoDemoLayout.BOARD_SIZE; row++)
            {
                for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
                {
                    if (paints[row, column] == colourId)
                    {
                        cells[cellIndex++] = new Vector2Int(column, row);
                    }
                }
            }

            return cells;
        }

        /// <summary>The moment the rightmost block of the colour in a row clearing from
        /// <paramref name="clearStart"/> — at column <paramref name="lastColumn"/> — is gone.</summary>
        private static float ColourGoneTime(float clearStart, int lastColumn)
            => InfoDemoChoreography.ClearCellShrinkStart(clearStart, lastColumn) + InfoDemoChoreography.CLEAR_SHRINK_DURATION;

        private static int CountInRow(int[,] paints, int row, int colourId)
        {
            int count = 0;
            for (int column = 0; column < InfoDemoLayout.BOARD_SIZE; column++)
            {
                if (paints[row, column] == colourId)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
