using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="PowerUpKind.Rotate"/> info demo (issue #449, mockup artboard "Döndür"), authored from
    /// the real rule (<c>PowerUpSystem.TryApplyRotate</c> / <c>CommitRotateSession</c>,
    /// <c>PieceRotator.TryRotateClockwise</c>): arm it, tap a dock piece and it turns 90° clockwise in its
    /// slot — the slot swapping to the catalog piece of that orientation. Turning is a free preview; the
    /// one charge is paid when the armed selection is dropped, and while Rotate is armed every press is an
    /// aim, so the player taps the icon again (the cancel gesture) before dragging the turned piece.
    /// <para>
    /// Choreography (5.2 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-3 ........
    /// row4 ....g...
    /// row5 ...uu...
    /// row6 ...ggbbu     (6,0)-(6,2) empty
    /// row7 .pppuugb     (7,0) empty
    /// </code>
    /// The strip holds the gold Rotate button (count 1) on the left and a single, a J (catalog
    /// <c>j_right</c>, a vertical bar with its foot bottom-right) and a 1x2 on the right.
    /// 0.5 s the button is pressed and arms · 1.15 s a finger taps the J · 1.2 s it turns a quarter
    /// clockwise about its own centre and at 1.55 s becomes the rotated catalog piece (<c>j_down</c>, a
    /// bar with its foot under the left end) · 1.9 s the button is tapped again: disarm, count 1 → 0 ·
    /// 2.2 s the turned piece lifts and lands on (6,0) · 3.0 s rows 6 and 7 are full and clear ·
    /// 3.4 s "Now it fits!" · hold, fade out from 4.8 s, loop.
    /// </para>
    /// </summary>
    internal static class RotateInfoDemo
    {
        internal const float LOOP_DURATION = 5.2f;

        internal const float PRESS_TIME = 0.5f;
        internal const float TAP_PIECE_TIME = 1.15f;
        internal const float ROTATE_START = 1.2f;
        internal const float ROTATE_DURATION = 0.32f;
        internal const float SWAP_TIME = 1.55f;
        internal const float DISARM_TIME = 1.9f;
        internal const float PLACE_START = 2.2f;
        internal const float CLEAR_START = 3.0f;
        internal const float LABEL_START = 3.4f;

        internal const int LAND_ROW = 6;
        internal const int LAND_COLUMN = 0;
        internal const int ROTATED_SLOT = 1;

        private const int BUTTON_COUNT = 1;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "........",
            "....g...",
            "...uu...",
            "...ggbbu",
            ".pppuugb",
        };

        /// <summary>The piece as dealt: a vertical bar of three with its foot at the bottom right — the
        /// catalog's <c>j_right</c>, (column, row) offsets with row 0 at the top.</summary>
        internal static readonly Vector2Int[] PieceShape =
        {
            new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2), new Vector2Int(1, 2),
        };

        /// <summary><see cref="PieceShape"/> turned 90° clockwise: a bar of three with its foot under the
        /// left end — the catalog's <c>j_down</c>, what <c>PieceRotator.TryRotateClockwise</c> returns
        /// for <c>j_right</c>.</summary>
        internal static readonly Vector2Int[] RotatedShape =
        {
            new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(0, 1),
        };

        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, PowerUpKind.Rotate, InfoDemoPaint.PLATE_GOLD, BUTTON_COUNT);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 slot = InfoDemoLayout.TraySlot(ROTATED_SLOT, InfoDemoTrayLayout.ChipLeft);
            builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), scale);
            int piece = builder.AddPiece(PieceShape, InfoDemoPaint.BLOCK_3, slot, scale);
            int rotatedPiece = builder.AddPiece(RotatedShape, InfoDemoPaint.BLOCK_3, slot, scale, 0f);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            // 1. Arm Rotate, tap the dock piece: it turns a quarter clockwise and the slot takes the rotated shape.
            InfoDemoPowerUpChoreography.PressButton(builder, button, PRESS_TIME, true, DISARM_TIME);
            InfoDemoChoreography.Tap(builder, TAP_PIECE_TIME, slot);
            InfoDemoTrayChoreography.RotatePiece(builder, piece, rotatedPiece, scale, ROTATE_START, ROTATE_DURATION, SWAP_TIME);

            // 2. Tapping the icon again drops the armed selection — that is when the one charge is paid.
            InfoDemoPowerUpChoreography.PressButton(builder, button, DISARM_TIME, false, DISARM_TIME);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, DISARM_TIME);

            // 3. The turned piece now completes rows 6 and 7 at once.
            InfoDemoChoreography.PlacePiece(
                builder, rotatedPiece, RotatedShape, InfoDemoPaint.BLOCK_3, slot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW, CLEAR_START);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW + 1, CLEAR_START);

            InfoDemoChoreography.FloatLabel(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_NOW_FITS,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, LAND_ROW - 0.2f),
                1.1f,
                InfoDemoPaint.PLATE_GOLD,
                LABEL_START,
                1.2f);

            return builder.Build();
        }
    }
}
