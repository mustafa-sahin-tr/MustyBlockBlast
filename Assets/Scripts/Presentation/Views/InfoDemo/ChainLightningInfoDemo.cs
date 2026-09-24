using System.Globalization;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="SpecialCellKind.ChainLightning"/> info demo (issue #450), authored from the real rule
    /// (<see cref="ChainLightningEffect"/>): a destroyed chain lightning vaporizes up to
    /// <see cref="ChainLightningEffect.MAX_TARGETS_PER_STRIKE"/> occupied cells drawn at random from
    /// anywhere on the board — never a neighbourhood, never a line. The demo's five targets are one such
    /// draw, deliberately spread across the board.
    /// <para>
    /// The demo shows only the effect. Chain lightning formation is switched off in the shipped game
    /// (issue #400, <c>BoardSystem.ChainLightningFormationEnabled</c>), so the card's body no longer
    /// promises a way to earn one.
    /// </para>
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ........
    /// row1 ..p.....     target (1,2)
    /// row2 ..u...g.     target (2,6)
    /// row3 .b..u...     target (3,4)
    /// row4 .g...b..     target (4,1)
    /// row5 .....gu.     target (5,6)
    /// row6 ..b.....
    /// row7 .pgbbuug     the chain lightning rides on (7,4); (7,0) is row 7's one gap
    /// </code>
    /// The tray holds an L corner, the single that will be played and a 1x2.
    /// 0.4 s the single lifts and lands on (7,0) · 1.2 s row 7 clears left to right, the chain lightning
    /// going with it · ~1.6 s five bolts leap from its cell to the five targets, 50 ms apart, each target
    /// vaporizing as its bolt arrives · 2.9 s "5 random blocks!" · hold, fade out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class ChainLightningInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const float PLACE_START = 0.4f;
        internal const float CLEAR_START = 1.2f;
        internal const float BOLT_STAGGER = 0.05f;
        internal const float LABEL_TIME = 2.9f;

        internal const int LIGHTNING_ROW = 7;
        internal const int LIGHTNING_COLUMN = 4;

        /// <summary>Where the single lands: row 7's one gap.</summary>
        internal const int LAND_ROW = LIGHTNING_ROW;
        internal const int LAND_COLUMN = 0;

        /// <summary>The first bolt leaves this long after the chain lightning starts to go.</summary>
        private const float BOLT_DELAY = 0.2f;

        internal static readonly string[] Rows =
        {
            "........",
            "..p.....",
            "..u...g.",
            ".b..u...",
            ".g...b..",
            ".....gu.",
            "..b.....",
            ".pgbbuug",
        };

        /// <summary>The strike's targets (x = column, y = row), in bolt order — one random draw of
        /// <see cref="ChainLightningEffect.MAX_TARGETS_PER_STRIKE"/> occupied cells.</summary>
        internal static readonly Vector2Int[] Targets =
        {
            new Vector2Int(2, 1),
            new Vector2Int(6, 2),
            new Vector2Int(1, 4),
            new Vector2Int(4, 3),
            new Vector2Int(6, 5),
        };

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>When the chain lightning starts to go with row 7's clear.</summary>
        internal static float LightningGoneTime()
            => InfoDemoChoreography.ClearCellShrinkStart(CLEAR_START, LIGHTNING_COLUMN);

        /// <summary>When bolt <paramref name="targetIndex"/> leaves the chain lightning's cell.</summary>
        internal static float BoltTime(int targetIndex) => LightningGoneTime() + BOLT_DELAY + (targetIndex * BOLT_STAGGER);

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_3, InfoDemoLayout.TraySlot(0), scale);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_1, singleSlot, scale);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(2), scale);

            int lightningIcon = InfoDemoSpecialCellChoreography.SpecialCell(
                builder, SpecialCellKind.ChainLightning, LIGHTNING_ROW, LIGHTNING_COLUMN, out int lightningHalo);
            int boltPaint = InfoDemoPaint.SpecialCellGlow(SpecialCellKind.ChainLightning);
            Vector2 source = InfoDemoLayout.Cell(LIGHTNING_ROW, LIGHTNING_COLUMN);

            // 1. The single completes row 7, which clears, taking the chain lightning with it.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_1, singleSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, LIGHTNING_ROW, CLEAR_START);
            InfoDemoSpecialCellChoreography.GoWithCell(builder, lightningIcon, lightningHalo, LightningGoneTime());

            // 2. Its strike: a bolt to each of five random blocks anywhere on the board, each vaporized as
            // its bolt lands.
            InfoDemoChoreography.Burst(builder, source, boltPaint, InfoDemoPaint.WHITE, 0.9f, 2.4f, BoltTime(0), 0.4f);
            Vector2Int[] target = new Vector2Int[1];
            for (int targetIndex = 0; targetIndex < Targets.Length; targetIndex++)
            {
                Vector2Int cell = Targets[targetIndex];
                Vector2 targetPosition = InfoDemoLayout.Cell(cell.y, cell.x);
                float boltTime = BoltTime(targetIndex);
                float strikeTime = boltTime + InfoDemoChoreography.BOLT_GROW_DURATION;

                InfoDemoChoreography.Bolt(builder, source, targetPosition, boltPaint, boltTime);
                InfoDemoChoreography.Burst(
                    builder, targetPosition, boltPaint, InfoDemoPaint.NONE, 0.9f, 2f, strikeTime, 0.35f);

                target[0] = cell;
                InfoDemoChoreography.ClearCells(builder, target, targetPosition, strikeTime, 0f);
            }

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_RANDOM_BLOCKS, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 6.2f),
                1f, InfoDemoPaint.INK, LABEL_TIME, 1.1f,
                ChainLightningEffect.MAX_TARGETS_PER_STRIKE.ToString(CultureInfo.InvariantCulture));

            return builder.Build();
        }
    }
}
