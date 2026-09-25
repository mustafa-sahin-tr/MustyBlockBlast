using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="ObjectiveType.ReinforcedCellsCleared"/> objective card's demo (issue #453, mockup artboard
    /// "Zırh"), authored from the real rule (<see cref="Board.TryDamage"/>, issue #438): a reinforced cell is a
    /// level-authored block with 2-3 hits. A clear that takes it while it has more than one hit left only spends
    /// one — the block stays, the rest of the line goes — and the clear that finds it on its last hit destroys it.
    /// Only that destruction counts (the target is always every reinforced cell the level authored). The armour
    /// is drawn exactly as the board draws it — the rock of <c>CellView.SetStageOverlay</c> (issue #480), one stage per hit
    /// left; there is no number on the cell. The objective has no parameter, so there is one demo.
    /// <para>
    /// Choreography (5.6 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ...u....
    /// row1 ...g....
    /// row2 ....u...     column 3 is full except (2,3)
    /// row3 ...b.g..
    /// row4 ...p....
    /// row5 ...g....
    /// row6 ...u.b..
    /// row7 ppgubb.u     (7,3) is the reinforced cell, 2 hits; (7,6) is row 7's gap
    /// </code>
    /// The strip holds the progress chip ("ARMOUR", 0/1) on the left and three tray pieces on the right.
    /// 0.4 s the first single fills (7,6) · 1.25 s row 7 clears around the armoured cell, which takes the hit —
    /// a knock, "-1", its armour drops to one stage · 2.2 s the second single fills (2,3), completing column 3 ·
    /// 3.05 s column 3 clears and the cell, now on its last hit, breaks · the chip ticks 0/1 → 1/1 · 3.7 s
    /// "Armour broken!" · hold, fade out from 5.2 s, loop.
    /// </para>
    /// </summary>
    internal static class ReinforcedCellsClearedInfoDemo
    {
        internal const float LOOP_DURATION = 5.6f;

        internal const int REINFORCED_ROW = 7;
        internal const int REINFORCED_COLUMN = 3;

        /// <summary>The cell's hits at loop start (<c>ReinforcedCellAuthoring.MIN_HIT_COUNT</c>).</summary>
        internal const int START_HIT_COUNT = 2;

        /// <summary>Which of the three armour skins the demo wears.</summary>
        internal const int SKIN = 0;

        internal const int FIRST_LAND_ROW = REINFORCED_ROW;
        internal const int FIRST_LAND_COLUMN = 6;
        internal const int SECOND_LAND_ROW = 2;
        internal const int SECOND_LAND_COLUMN = REINFORCED_COLUMN;

        internal const float FIRST_PLACE_START = 0.4f;
        internal const float FIRST_CLEAR_START = 1.25f;
        internal const float SECOND_PLACE_START = 2.2f;
        internal const float SECOND_CLEAR_START = 3.05f;
        internal const float CHIP_DELAY = 0.15f;
        internal const float LABEL_START = 3.7f;

        internal static readonly string[] Rows =
        {
            "...u....",
            "...g....",
            "....u...",
            "...b.g..",
            "...p....",
            "...g....",
            "...u.b..",
            "ppgubb.u",
        };

        internal static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };

        /// <summary>When row 7's clear reaches the reinforced cell and it spends a hit.</summary>
        internal static float HitTime() => InfoDemoChoreography.ClearCellShrinkStart(FIRST_CLEAR_START, REINFORCED_COLUMN);

        /// <summary>When column 3's clear reaches the cell on its last hit and it breaks.</summary>
        internal static float BreakTime() => InfoDemoChoreography.ClearCellShrinkStart(SECOND_CLEAR_START, REINFORCED_ROW);

        internal static InfoDemoTimeline Build() => Build(out _, out _);

        /// <summary>As <see cref="Build()"/>, handing back the chip and the armour layer so a test can read them.</summary>
        internal static InfoDemoTimeline Build(out InfoDemoProgressChip chip, out int armourLayer)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            chip = InfoDemoChoreography.ProgressChip(builder, LocalizationKeys.INFO_POPUP_DEMO_CHIP_ARMOUR, null, 0, 1);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 firstSlot = InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft);
            Vector2 secondSlot = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            int first = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_4, firstSlot, scale);
            int second = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_2, secondSlot, scale);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            armourLayer = builder.AddCellLayer(
                InfoDemoCellLayer.Armour, REINFORCED_ROW, REINFORCED_COLUMN, START_HIT_COUNT, SKIN);

            // 1. Row 7 clears around the armoured cell: it only spends a hit and stays.
            InfoDemoChoreography.PlacePiece(
                builder, first, SingleShape, InfoDemoPaint.BLOCK_4, firstSlot, FIRST_LAND_ROW, FIRST_LAND_COLUMN, FIRST_PLACE_START);
            InfoDemoChoreography.ClearRowSparing(builder, REINFORCED_ROW, REINFORCED_COLUMN, FIRST_CLEAR_START);
            float hit = HitTime();
            InfoDemoSpecialCellChoreography.StepLayer(builder, armourLayer, START_HIT_COUNT - 1, hit);
            Vector2 cell = InfoDemoLayout.Cell(REINFORCED_ROW, REINFORCED_COLUMN);
            InfoDemoChoreography.FloatText(
                builder, "-1", cell + new Vector2(0.15f, -0.75f), 0.6f, InfoDemoPaint.BADGE_RED, 0.45f, hit, 0.9f);

            // 2. Column 3 clears it again, on its last hit: it breaks, and the goal counts it.
            InfoDemoChoreography.PlacePiece(
                builder, second, SingleShape, InfoDemoPaint.BLOCK_2, secondSlot, SECOND_LAND_ROW, SECOND_LAND_COLUMN,
                SECOND_PLACE_START);
            InfoDemoChoreography.ClearColumn(builder, REINFORCED_COLUMN, SECOND_CLEAR_START);
            float broken = BreakTime();
            InfoDemoSpecialCellChoreography.LayerGoes(builder, armourLayer, broken);
            InfoDemoChoreography.AdvanceChip(builder, chip, broken + CHIP_DELAY);

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_ARMOUR_BROKEN, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 5.6f),
                1.0f, InfoDemoPaint.INK, LABEL_START, 1.3f);

            return builder.Build();
        }
    }
}
