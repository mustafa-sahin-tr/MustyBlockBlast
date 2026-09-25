using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.DiamondsCleared"/> objective card's demo (issue #453, mockup artboard
    /// "Elmas"), authored from the real mechanic:
    /// <list type="bullet">
    /// <item>Diamonds are not injected as special pieces. While a Path level's Diamonds Cleared objective is
    /// unfinished, every ordinary deal of a piece of 2+ cells rolls the level's decoration chance
    /// (<c>DiamondPieceDecorator.TryDecorate</c>) and, on a hit, puts gems of the objective's colour on 1 to
    /// (cells - 1) of its cells — never all of them. The demo's 1x3 carries two.</item>
    /// <item>A decorated cell lands as a <see cref="SpecialCellKind.Diamond"/> cell
    /// (<c>BoardSystem.TryPlacePiece</c>, <see cref="DiamondCellRules.CanCarryDiamond"/>) and is collected when
    /// that block is destroyed; progress counts gems of the objective's colour destroyed, however many one
    /// clear takes (<c>DestroyedDiamondCountOf</c>). The gem's colour is its own, not the block's.</item>
    /// </list>
    /// One demo per gem colour id (<see cref="Build(int)"/>), drawn in the current theme's fill for it exactly as
    /// <c>DiamondVisuals</c> tints it; the chip is illustrative (0/2 → 2/2 — levels ask for 10).
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ........
    /// row1 ..g.....
    /// row2 ....u...
    /// row3 .b......
    /// row4 ...g..u.
    /// row5 ..bb....
    /// row6 g....g..
    /// row7 ppg...uu     (7,3)-(7,5) is row 7's gap
    /// </code>
    /// The strip holds the progress chip (a gem of the colour, 0/2) on the left and three tray pieces on the
    /// right: the decorated 1x3 — gems on its outer cells — an L corner and a single.
    /// 0.45 s the 1x3 lifts and glides onto (7,3)-(7,5), its gems landing with it · 1.3 s row 7 clears; each gem
    /// goes with its block and flies to the chip, which ticks 0/2 → 1/2 → 2/2 as they arrive · 2.3 s "Diamonds
    /// collected!" · hold, fade out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class DiamondsClearedInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const int LAND_ROW = 7;
        internal const int LAND_COLUMN = 3;

        internal const float PLACE_START = 0.45f;
        internal const float CLEAR_START = 1.3f;
        internal const float FLY_DURATION = 0.6f;
        internal const float LABEL_START = 2.3f;

        private const float FLY_SIZE = 0.8f;
        private const float FLY_ARC = 1.2f;

        internal static readonly string[] Rows =
        {
            "........",
            "..g.....",
            "....u...",
            ".b......",
            "...g..u.",
            "..bb....",
            "g....g..",
            "ppg...uu",
        };

        internal static readonly Vector2Int[] LineShape = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0) };

        /// <summary>Which of <see cref="LineShape"/>'s cells carry a gem: the two outer ones — one fewer than
        /// the piece's cell count, the most <c>DiamondPieceDecorator</c> ever decorates.</summary>
        internal static readonly bool[] Decorated = { true, false, true };

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };

        /// <summary>The block colours the decorated piece may be drawn in, first choice first — any but the
        /// gem's, so the gem reads against its block.</summary>
        private static readonly int[] PieceColourPreference =
            { InfoDemoPaint.BLOCK_5, InfoDemoPaint.BLOCK_3, InfoDemoPaint.BLOCK_2 };

        /// <summary>True when <paramref name="colourId"/> is a gem colour id the demo can draw.</summary>
        internal static bool Supports(int colourId) => colourId >= 1 && colourId <= Board.COLOUR_COUNT;

        /// <summary>How many gems the demo collects — its chip target.</summary>
        internal static int GemCount
        {
            get
            {
                int count = 0;
                for (int cellIndex = 0; cellIndex < Decorated.Length; cellIndex++)
                {
                    if (Decorated[cellIndex])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>The decorated piece's block colour for gem colour <paramref name="colourId"/>.</summary>
        internal static int PiecePaint(int colourId)
            => PieceColourPreference[0] != colourId ? PieceColourPreference[0] : PieceColourPreference[1];

        /// <summary>The per-cell gem colour ids of the decorated piece, parallel to <see cref="LineShape"/>
        /// (0 for an undecorated cell) — the <c>TrayModel</c> decoration it stands for.</summary>
        internal static int[] DiamondColourIds(int colourId)
        {
            int[] ids = new int[LineShape.Length];
            for (int cellIndex = 0; cellIndex < ids.Length; cellIndex++)
            {
                ids[cellIndex] = Decorated[cellIndex] ? colourId : 0;
            }

            return ids;
        }

        /// <summary>When the gem on board column <paramref name="column"/> of row 7 goes with its block.</summary>
        internal static float GemGoneTime(int column) => InfoDemoChoreography.ClearCellShrinkStart(CLEAR_START, column);

        internal static InfoDemoTimeline Build(int colourId) => Build(colourId, out _);

        /// <summary>As <see cref="Build(int)"/>, handing back the chip so a test can read it.</summary>
        internal static InfoDemoTimeline Build(int colourId, out InfoDemoProgressChip chip)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            chip = InfoDemoChoreography.ProgressChip(builder, InfoDemoSprite.DiamondIcon, 0, colourId, 0, GemCount);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            int piecePaint = PiecePaint(colourId);
            Vector2 lineSlot = InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft);
            int line = builder.AddDiamondPiece(LineShape, piecePaint, DiamondColourIds(colourId), lineSlot, scale);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft), scale);
            builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_4, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            // 1. The decorated piece lands; its gems are now diamond cells on the board.
            float landTime = InfoDemoChoreography.PlacePiece(
                builder, line, LineShape, piecePaint, lineSlot, LAND_ROW, LAND_COLUMN, PLACE_START);

            // 2. Row 7 clears; each gem goes with its block and flies to the chip.
            InfoDemoChoreography.ClearRow(builder, LAND_ROW, CLEAR_START);
            for (int cellIndex = 0; cellIndex < LineShape.Length; cellIndex++)
            {
                if (!Decorated[cellIndex])
                {
                    continue;
                }

                int column = LAND_COLUMN + LineShape[cellIndex].x;
                int gem = builder.AddCellLayer(InfoDemoCellLayer.Diamond, LAND_ROW, column, colourId, 0, 0f);
                InfoDemoSpecialCellChoreography.LayerAppears(builder, gem, landTime);

                float gone = GemGoneTime(column);
                InfoDemoSpecialCellChoreography.LayerGoes(builder, gem, gone);
                InfoDemoHudChoreography.FlyTo(
                    builder, InfoDemoSprite.DiamondIcon, 0, FLY_SIZE, InfoDemoLayout.Cell(LAND_ROW, column),
                    InfoDemoChoreography.ChipGlyphCentre, gone, FLY_DURATION, FLY_ARC, colourId);
                InfoDemoChoreography.AdvanceChip(builder, chip, gone + FLY_DURATION);
            }

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_DIAMONDS_COLLECTED,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 5.0f), 1.0f, InfoDemoPaint.INK, LABEL_START, 1.3f);

            return builder.Build();
        }
    }
}
