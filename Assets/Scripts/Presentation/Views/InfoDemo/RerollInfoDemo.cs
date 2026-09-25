using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="PowerUpKind.Reroll"/> info demo (issue #449, mockup artboard "Yeniden Çek"), authored
    /// from the real rule (<c>PowerUpSystem.TryApplyReroll</c>): targetless, so it is applied on the tap
    /// itself (never armed) — all three dock pieces are discarded and three new ones drawn, and that draw
    /// is the game's only solvability-guaranteed one: at least one of them fits.
    /// <para>
    /// Choreography (5.4 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 gg.bbuu.
    /// row1 ubbgg.pp
    /// row2 gpp.bbgu
    /// row3 bbgguu.g     no 3x3, 1x5 or 4-cell L fits anywhere:
    /// row4 pu.ggbbp     the largest empty group is (6,6),(6,7),(7,6)
    /// row5 ggbb.upu
    /// row6 uuppgg..
    /// row7 bbuugg.p
    /// </code>
    /// The strip holds the teal Reroll button (count 1) and a dock of a 3x3, a 1x5 and an L.
    /// 0.3 s a red cross pops on each piece · 0.55 s "Nothing fits" · 1.05 s the button is pressed, count
    /// 1 → 0 · 1.15 s the three pieces drop away · 1.5 / 1.6 / 1.7 s a 1x2, a single and a 2x2 are dealt ·
    /// 2.05 s a green ring picks out the 1x2 · 2.55 s it lands on (6,6)-(6,7) · 3.35 s row 6 clears · hold,
    /// fade out from 5.0 s, loop.
    /// </para>
    /// </summary>
    internal static class RerollInfoDemo
    {
        internal const float LOOP_DURATION = 5.4f;

        internal const float REJECT_TIME = 0.3f;
        internal const float LABEL_START = 0.55f;
        internal const float PRESS_TIME = 1.05f;
        internal const float DISCARD_TIME = 1.15f;
        internal const float DEAL_START = 1.5f;
        internal const float DEAL_STAGGER = 0.1f;
        internal const float RING_START = 2.05f;
        internal const float PLACE_START = 2.55f;
        internal const float CLEAR_START = 3.35f;

        internal const int LAND_ROW = 6;
        internal const int LAND_COLUMN = 6;

        private const int BUTTON_COUNT = 1;
        private const float BIG_PIECE_SCALE = 0.36f;
        private const float LONG_PIECE_SCALE = 0.3f;

        internal static readonly string[] Rows =
        {
            "gg.bbuu.",
            "ubbgg.pp",
            "gpp.bbgu",
            "bbgguu.g",
            "pu.ggbbp",
            "ggbb.upu",
            "uuppgg..",
            "bbuugg.p",
        };

        /// <summary>The dock before the reroll (none fits the board above), slot by slot.</summary>
        internal static readonly Vector2Int[][] OldShapes =
        {
            Rectangle(3, 3),
            Rectangle(5, 1),
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2), new Vector2Int(1, 2) },
        };

        /// <summary>The dock after it, slot by slot: a 1x2 (the one that fits), a single and a 2x2.</summary>
        internal static readonly Vector2Int[][] NewShapes =
        {
            Rectangle(2, 1),
            Rectangle(1, 1),
            Rectangle(2, 2),
        };

        private static readonly int[] OldPaints = { InfoDemoPaint.BLOCK_3, InfoDemoPaint.BLOCK_1, InfoDemoPaint.BLOCK_5 };
        private static readonly int[] NewPaints = { InfoDemoPaint.BLOCK_2, InfoDemoPaint.BLOCK_3, InfoDemoPaint.BLOCK_1 };
        private static readonly float[] OldScales = { BIG_PIECE_SCALE, LONG_PIECE_SCALE, InfoDemoLayout.TRAY_PIECE_SCALE };

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, PowerUpKind.Reroll, InfoDemoPaint.PLATE_REROLL, BUTTON_COUNT);

            float trayScale = InfoDemoLayout.TRAY_PIECE_SCALE;
            int[] newPieces = new int[NewShapes.Length];

            for (int slotIndex = 0; slotIndex < InfoDemoLayout.TRAY_SLOT_COUNT; slotIndex++)
            {
                Vector2 slot = InfoDemoLayout.TraySlot(slotIndex, InfoDemoTrayLayout.ChipLeft);

                // 1. The dock as dealt: none of it fits, and each piece says so.
                int oldPiece = builder.AddPiece(OldShapes[slotIndex], OldPaints[slotIndex], slot, OldScales[slotIndex]);
                InfoDemoTrayChoreography.RejectMark(
                    builder, TopRightCorner(OldShapes[slotIndex], slot, OldScales[slotIndex]), REJECT_TIME + (slotIndex * 0.05f),
                    DISCARD_TIME);

                // 3. ...the whole dock is discarded and three new pieces are drawn.
                InfoDemoTrayChoreography.DiscardPiece(builder, oldPiece, slot, OldScales[slotIndex], DISCARD_TIME);
                newPieces[slotIndex] = builder.AddPiece(NewShapes[slotIndex], NewPaints[slotIndex], slot, trayScale, 0f);
                InfoDemoTrayChoreography.DealPiece(builder, newPieces[slotIndex], trayScale, DEAL_START + (slotIndex * DEAL_STAGGER));
            }

            InfoDemoHudChoreography.LabelPill(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_NOTHING_FITS,
                new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 3.5f),
                new Vector2(5.4f, 1f),
                0.55f,
                InfoDemoPaint.BADGE_RED,
                LABEL_START,
                PRESS_TIME);

            // 2. Reroll is targetless: the tap applies it and spends the charge.
            InfoDemoPowerUpChoreography.PressButton(builder, button, PRESS_TIME, false, PRESS_TIME);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, PRESS_TIME);

            // 4. At least one new piece fits: the 1x2 completes row 6.
            Vector2 fitSlot = InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft);
            InfoDemoTrayChoreography.HighlightRing(
                builder, fitSlot, new Vector2(1.45f, 1.05f), InfoDemoLayout.FromMockLength(2.5f), InfoDemoPaint.SUCCESS,
                RING_START, PLACE_START, 1);
            InfoDemoChoreography.PlacePiece(
                builder, newPieces[0], NewShapes[0], NewPaints[0], fitSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW, CLEAR_START);

            return builder.Build();
        }

        private static Vector2Int[] Rectangle(int columns, int rows)
        {
            Vector2Int[] cells = new Vector2Int[columns * rows];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    cells[(row * columns) + column] = new Vector2Int(column, row);
                }
            }

            return cells;
        }

        /// <summary>Where a "doesn't fit" mark hangs on a tray piece: its bounding box's top-right corner.</summary>
        private static Vector2 TopRightCorner(Vector2Int[] shape, Vector2 centre, float scale)
        {
            InfoDemoLayout.ShapeBounds(shape, out Vector2Int min, out Vector2Int max);
            float halfWidth = (max.x - min.x + 1) * scale * 0.5f;
            float halfHeight = (max.y - min.y + 1) * scale * 0.5f;
            return centre + new Vector2(halfWidth, -halfHeight);
        }
    }
}
