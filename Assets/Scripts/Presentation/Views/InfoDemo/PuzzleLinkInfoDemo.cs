using System.Globalization;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="SpecialCellKind.PuzzleLink"/> info demo (issue #483), authored from the real rule
    /// (<see cref="Board.ResolvePuzzleLinks"/>): a group goes only when every piece is hit in the same
    /// move. Two vertical pairs: a single clears one row through the left pair's lower piece — the row
    /// clears around it and the pair stays — then a vertical domino clears two rows at once through both
    /// pieces of the right pair, which goes whole and pays its bonus.
    /// <para>
    /// Choreography (5.6 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row5 gupgBp.u     B = right pair's top piece (5,4); (5,6) gap
    /// row6 puAuBg.b     A = left pair's top (6,2), B = right pair's bottom (6,4); (6,6) gap
    /// row7 ubApgub.     A = left pair's bottom (7,2); (7,7) gap
    /// </code>
    /// 0.4 s the single lands on (7,7) · 1.2 s row 7 clears around the left pair, which shudders and stays ·
    /// 2.1 s the domino lands on (5,6)-(6,6) · 2.9 s rows 5 and 6 clear: the right pair goes whole, "+20"
    /// · hold, fade out from 5.2 s, loop.
    /// </para>
    /// </summary>
    internal static class PuzzleLinkInfoDemo
    {
        internal const float LOOP_DURATION = 5.6f;

        internal const float FIRST_PLACE_START = 0.4f;
        internal const float FIRST_CLEAR_START = 1.2f;
        internal const float SECOND_PLACE_START = 2.1f;
        internal const float SECOND_CLEAR_START = 2.9f;

        internal const int LEFT_PAIR_COLUMN = 2;
        internal const int LEFT_PAIR_TOP_ROW = 6;
        internal const int RIGHT_PAIR_COLUMN = 4;
        internal const int RIGHT_PAIR_TOP_ROW = 5;

        /// <summary>The right pair's bonus: two link cells at <see cref="ScoreRules.PUZZLE_LINK_BONUS_PER_CELL"/>.</summary>
        internal const int PAIR_BONUS = 2 * ScoreRules.PUZZLE_LINK_BONUS_PER_CELL;

        private const float TOOTH_WIDTH = 0.34f;
        private const float TOOTH_HEIGHT = 0.3f;
        private const float SHUDDER_DURATION = 0.35f;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "....g...",
            "..u.....",
            ".....p..",
            "gupgbp.u",
            "pugubg.b",
            "ubgpgub.",
        };

        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        internal static readonly Vector2Int[] VerticalDominoShape = { new Vector2Int(0, 0), new Vector2Int(0, 1) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };

        /// <summary>When rows 5 and 6 reach the right pair and take it, whole.</summary>
        internal static float RightPairGoneTime() => InfoDemoChoreography.ClearCellShrinkStart(SECOND_CLEAR_START, RIGHT_PAIR_COLUMN);

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 singleSlot = InfoDemoLayout.TraySlot(0);
            Vector2 dominoSlot = InfoDemoLayout.TraySlot(1);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_2, singleSlot, scale);
            int domino = builder.AddPiece(VerticalDominoShape, InfoDemoPaint.BLOCK_5, dominoSlot, scale);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_1, InfoDemoLayout.TraySlot(2), scale);

            int leftTop = InfoDemoSpecialCellChoreography.SpecialCell(
                builder, SpecialCellKind.PuzzleLink, LEFT_PAIR_TOP_ROW, LEFT_PAIR_COLUMN, out int _);
            int leftBottom = InfoDemoSpecialCellChoreography.SpecialCell(
                builder, SpecialCellKind.PuzzleLink, LEFT_PAIR_TOP_ROW + 1, LEFT_PAIR_COLUMN, out int _);
            int rightTop = InfoDemoSpecialCellChoreography.SpecialCell(
                builder, SpecialCellKind.PuzzleLink, RIGHT_PAIR_TOP_ROW, RIGHT_PAIR_COLUMN, out int rightTopBlock);
            int rightBottom = InfoDemoSpecialCellChoreography.SpecialCell(
                builder, SpecialCellKind.PuzzleLink, RIGHT_PAIR_TOP_ROW + 1, RIGHT_PAIR_COLUMN, out int rightBottomBlock);

            // The teeth that lock each pair together, drawn over both pieces as the board draws them.
            AddTooth(builder, LEFT_PAIR_TOP_ROW, LEFT_PAIR_COLUMN);
            int rightTooth = AddTooth(builder, RIGHT_PAIR_TOP_ROW, RIGHT_PAIR_COLUMN);

            // 1. The single completes row 7, which clears around the left pair's lower piece: only half the
            //    pair was hit, so none of it goes.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_2, singleSlot, 7, 7, FIRST_PLACE_START);
            InfoDemoChoreography.ClearRowSparing(builder, 7, LEFT_PAIR_COLUMN, FIRST_CLEAR_START);
            float shudder = InfoDemoChoreography.ClearCellShrinkStart(FIRST_CLEAR_START, LEFT_PAIR_COLUMN);
            builder.Scale(leftTop, shudder, SHUDDER_DURATION, 1f, 1.12f, InfoDemoEasing.Pulse);
            builder.Scale(leftBottom, shudder, SHUDDER_DURATION, 1f, 1.12f, InfoDemoEasing.Pulse);

            // 2. The domino completes rows 5 and 6 at once, through both pieces of the right pair: the pair
            //    goes whole and pays its bonus. Row 6 still spares the left pair's upper piece.
            InfoDemoChoreography.PlacePiece(
                builder, domino, VerticalDominoShape, InfoDemoPaint.BLOCK_5, dominoSlot, RIGHT_PAIR_TOP_ROW, 6,
                SECOND_PLACE_START);
            InfoDemoChoreography.ClearRow(builder, RIGHT_PAIR_TOP_ROW, SECOND_CLEAR_START);
            InfoDemoChoreography.ClearRowSparing(builder, RIGHT_PAIR_TOP_ROW + 1, LEFT_PAIR_COLUMN, SECOND_CLEAR_START);

            float gone = RightPairGoneTime();
            InfoDemoSpecialCellChoreography.GoWithCell(builder, rightTop, rightTopBlock, gone);
            InfoDemoSpecialCellChoreography.GoWithCell(builder, rightBottom, rightBottomBlock, gone);
            builder.Fade(rightTooth, gone, InfoDemoSpecialCellChoreography.EXIT_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);
            InfoDemoChoreography.FloatText(
                builder, "+" + PAIR_BONUS.ToString(CultureInfo.InvariantCulture),
                InfoDemoLayout.Cell(RIGHT_PAIR_TOP_ROW, RIGHT_PAIR_COLUMN) + new Vector2(0.9f, -0.2f), 0.8f,
                InfoDemoPaint.SUCCESS, 0.75f, gone + 0.15f, 1.3f);

            return builder.Build();
        }

        /// <summary>A tooth between the piece at (<paramref name="topRow"/>, <paramref name="column"/>) and
        /// the one below it. Returns its element id.</summary>
        private static int AddTooth(InfoDemoTimelineBuilder builder, int topRow, int column)
        {
            Vector2 between = InfoDemoLayout.Cell(topRow, column) + new Vector2(0f, 0.5f);
            return builder.AddPanel(between, new Vector2(TOOTH_WIDTH, TOOTH_HEIGHT), TOOTH_HEIGHT * 0.35f, InfoDemoPaint.BADGE_RED);
        }
    }
}
