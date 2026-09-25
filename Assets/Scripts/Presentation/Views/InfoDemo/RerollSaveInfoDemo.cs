using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.RerollSave"/> objective card's demo (issue #454, mockup artboard "Yeniden
    /// çekle kurtarış"), authored from the real rule (<c>BoardSystem.TryRerollTray</c>,
    /// <see cref="ObjectiveProgress.ApplyPowerUpRerollSave"/>): a Reroll counts when, the moment it is spent,
    /// no dock piece has a legal placement anywhere — the Hold pocket's piece deliberately left out of that
    /// check — and the fresh draw has at least one. A Reroll spent with a move still on offer never counts.
    /// <para>
    /// Why the pocket is drawn: the run ends the moment neither the dock nor a retrievable parked piece can
    /// move (<c>BoardSystem.CheckGameOver</c>), so a dead dock with the run still going only exists while the
    /// pocket holds a piece that fits and a Hold charge to swap it back. The demo shows exactly that state: a
    /// single parked in the pocket (charge 1), and a dock of a 3x3, a 1x5 and a 4-cell L that fit nowhere on
    /// <see cref="RerollInfoDemo.Rows"/>.
    /// </para>
    /// <para>
    /// Choreography (5.4 s loop): the strip holds, left to right, the chip ("SAVE"), the teal Reroll button
    /// (count 1), the dock and the pocket. 0.3 s a red cross pops on each dock piece · 0.55 s "Nothing fits" ·
    /// 1.05 s the button is pressed, count 1 → 0 · 1.15 s the dock is discarded · 1.5 s a 1x2, a single and a
    /// 2x2 are dealt · 1.9 s the chip ticks with the check · 2.0 s "Saved!" · 2.4 s a green ring picks out the
    /// 1x2 · 2.85 s it lands on (6,6)-(6,7) · 3.7 s row 6 clears · hold, fade out from 5.0 s, loop.
    /// </para>
    /// </summary>
    internal static class RerollSaveInfoDemo
    {
        internal const float LOOP_DURATION = 5.4f;

        internal const float REJECT_TIME = 0.3f;
        internal const float NOTHING_FITS_START = 0.55f;
        internal const float PRESS_TIME = 1.05f;
        internal const float DISCARD_TIME = 1.15f;
        internal const float DEAL_START = 1.5f;
        internal const float DEAL_STAGGER = 0.1f;
        internal const float CHIP_ADVANCE_TIME = 1.9f;
        internal const float SAVED_LABEL_START = 2.0f;
        internal const float RING_START = 2.4f;
        internal const float PLACE_START = 2.85f;
        internal const float CLEAR_START = 3.7f;

        internal const int LAND_ROW = RerollInfoDemo.LAND_ROW;
        internal const int LAND_COLUMN = RerollInfoDemo.LAND_COLUMN;

        /// <summary>The largest target the chip can print without crowding it.</summary>
        internal const int MAX_TARGET = 999;

        /// <summary>Mockup-unit strip layout, left to right after the chip: the Reroll button, the three dock
        /// slots and the Hold pocket (its side, and its charge badge's size).</summary>
        internal const float MOCK_BUTTON_X = 94f;
        internal const float MOCK_POCKET_X = 238f;
        private const float MOCK_POCKET_SIDE = 30f;
        private const float MOCK_POCKET_BADGE_DIAMETER = 12f;
        private const float MOCK_POCKET_BADGE_RIM = 1.5f;
        private const float MOCK_POCKET_BADGE_FONT = 8f;
        private const float MOCK_POCKET_BADGE_INSET = 2f;

        private static readonly float[] MockSlotX = { 140f, 175f, 208f };

        /// <summary>The dock's resting scales before the reroll — shrunk to fit the narrower slots beside the
        /// button and the pocket — slot by slot.</summary>
        private static readonly float[] OldScales = { 0.3f, 0.2f, 0.3f };

        private static readonly int[] OldPaints = { InfoDemoPaint.BLOCK_3, InfoDemoPaint.BLOCK_1, InfoDemoPaint.BLOCK_5 };
        private static readonly int[] NewPaints = { InfoDemoPaint.BLOCK_2, InfoDemoPaint.BLOCK_3, InfoDemoPaint.BLOCK_1 };

        private const int BUTTON_COUNT = 1;
        private const int HOLD_CHARGE_COUNT = 1;

        /// <summary>The piece parked in the pocket: a single, which fits the board.</summary>
        internal static readonly Vector2Int[] HeldShape = { new Vector2Int(0, 0) };

        /// <summary>True when the chip can show <paramref name="target"/>.</summary>
        internal static bool Supports(int target) => target >= 1 && target <= MAX_TARGET;

        /// <summary>The chip's value at loop start for <paramref name="target"/>: one short of it.</summary>
        internal static int ChipStart(int target) => Mathf.Max(0, target - 1);

        /// <summary>Centre of dock slot <paramref name="slotIndex"/> in this demo's strip.</summary>
        internal static Vector2 Slot(int slotIndex) => InfoDemoLayout.StripPoint(MockSlotX[slotIndex]);

        internal static InfoDemoTimeline Build(int target) => Build(target, out _, out _);

        /// <summary>As <see cref="Build(int)"/>, handing back the chip and the Reroll button so tests can read them.</summary>
        internal static InfoDemoTimeline Build(int target, out InfoDemoProgressChip chip, out InfoDemoPowerUpButton button)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, RerollInfoDemo.Rows);

            // Strip: the goal chip, the Reroll button, the dock and the pocket with its parked single.
            chip = InfoDemoChoreography.ProgressChip(
                builder, LocalizationKeys.INFO_POPUP_DEMO_CHIP_SAVE, null, ChipStart(target), target);
            button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, PowerUpKind.Reroll, InfoDemoPaint.PLATE_REROLL, BUTTON_COUNT, InfoDemoLayout.StripPoint(MOCK_BUTTON_X));
            AddHeldPiece(builder);

            int[] newPieces = new int[InfoDemoLayout.TRAY_SLOT_COUNT];
            for (int slotIndex = 0; slotIndex < InfoDemoLayout.TRAY_SLOT_COUNT; slotIndex++)
            {
                Vector2 slot = Slot(slotIndex);
                Vector2Int[] oldShape = RerollInfoDemo.OldShapes[slotIndex];

                // 1. The dock as it stands: none of it fits anywhere, and each piece says so.
                int oldPiece = builder.AddPiece(oldShape, OldPaints[slotIndex], slot, OldScales[slotIndex]);
                InfoDemoTrayChoreography.RejectMark(
                    builder, InfoDemoLayout.TopRightCorner(oldShape, slot, OldScales[slotIndex]),
                    REJECT_TIME + (slotIndex * 0.05f), DISCARD_TIME);

                // 3. ...the whole dock is discarded and three new pieces are drawn — the pocket is untouched.
                InfoDemoTrayChoreography.DiscardPiece(builder, oldPiece, slot, OldScales[slotIndex], DISCARD_TIME);
                newPieces[slotIndex] = builder.AddPiece(
                    RerollInfoDemo.NewShapes[slotIndex], NewPaints[slotIndex], slot, InfoDemoLayout.TRAY_PIECE_SCALE, 0f);
                InfoDemoTrayChoreography.DealPiece(
                    builder, newPieces[slotIndex], InfoDemoLayout.TRAY_PIECE_SCALE, DEAL_START + (slotIndex * DEAL_STAGGER));
            }

            float centreX = (InfoDemoLayout.BOARD_SIZE - 1) * 0.5f;
            InfoDemoHudChoreography.LabelPill(
                builder, LocalizationKeys.INFO_POPUP_DEMO_NOTHING_FITS, new Vector2(centreX, 3.5f), new Vector2(5.4f, 1f),
                0.55f, InfoDemoPaint.BADGE_RED, NOTHING_FITS_START, PRESS_TIME);

            // 2. Reroll is targetless: the tap applies it and spends the charge.
            InfoDemoPowerUpChoreography.PressButton(builder, button, PRESS_TIME, false, PRESS_TIME);
            InfoDemoPowerUpChoreography.SpendCharge(builder, button, PRESS_TIME);

            // 4. A dead dock rerolled into one with a move: the save counts.
            InfoDemoChoreography.AdvanceChip(builder, chip, CHIP_ADVANCE_TIME);
            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_SAVED, new Vector2(centreX, 3.5f), 1.0f, InfoDemoPaint.INK,
                SAVED_LABEL_START, 1.3f);

            // 5. And the move is real: the 1x2 completes row 6.
            Vector2 fitSlot = Slot(0);
            InfoDemoTrayChoreography.HighlightRing(
                builder, fitSlot, new Vector2(1.2f, 0.95f), InfoDemoLayout.FromMockLength(2.5f), InfoDemoPaint.SUCCESS,
                RING_START, PLACE_START, 1);
            InfoDemoChoreography.PlacePiece(
                builder, newPieces[0], RerollInfoDemo.NewShapes[0], NewPaints[0], fitSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW, CLEAR_START);

            return builder.Build();
        }

        /// <summary>The Hold pocket with its parked single and its charge badge — what keeps the run alive while
        /// the dock is dead.</summary>
        private static void AddHeldPiece(InfoDemoTimelineBuilder builder)
        {
            Vector2 pocket = InfoDemoLayout.StripPoint(MOCK_POCKET_X);
            HoldInfoDemo.AddPocketWell(builder, pocket, MOCK_POCKET_SIDE);
            builder.AddPiece(HeldShape, InfoDemoPaint.BLOCK_2, pocket, InfoDemoLayout.TRAY_PIECE_SCALE);

            float badgeOffset = InfoDemoLayout.FromMockLength((MOCK_POCKET_SIDE * 0.5f) - MOCK_POCKET_BADGE_INSET);
            InfoDemoHudChoreography.CountBadge(
                builder,
                pocket + new Vector2(badgeOffset, -badgeOffset),
                HOLD_CHARGE_COUNT,
                InfoDemoPaint.HOLD_BADGE,
                InfoDemoLayout.FromMockLength(MOCK_POCKET_BADGE_DIAMETER),
                InfoDemoLayout.FromMockLength(MOCK_POCKET_BADGE_RIM),
                InfoDemoLayout.FromMockLength(MOCK_POCKET_BADGE_FONT),
                true);
        }
    }
}
