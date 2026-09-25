using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="SpecialCellKind.Laser"/> info demo (issue #450), authored from the real rule
    /// (<see cref="LaserEffect"/>): a laser wipes the whole line at right angles to whatever destroyed it —
    /// taken out by a row clear it wipes its column, full or not, edge to edge.
    /// <para>
    /// The demo shows only the effect. Laser formation is switched off in the shipped game (issue #399,
    /// <c>LaserSpawnSystem.FormationEnabled</c>), so the card's body no longer promises a way to earn one.
    /// </para>
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ........
    /// row1 ...g....
    /// row2 ..pb....
    /// row3 ...u.g..
    /// row4 .g.g....
    /// row5 ...p.u..
    /// row6 ggbbuup.     the laser rides on (6,3); (6,7) is row 6's one gap
    /// row7 u..g..bb
    /// </code>
    /// The tray holds an L corner, the single that will be played and a 1x2.
    /// 0.4 s the single lifts and lands on (6,7) · 1.2 s row 6 is full and clears left to right, the
    /// laser going with it · ~1.6 s a vertical beam runs down column 3 · 1.7 s column 3's blocks clear
    /// from the laser's cell outwards · 2.8 s "Perpendicular line!" · hold, fade out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class LaserInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const float PLACE_START = 0.4f;
        internal const float CLEAR_START = 1.2f;
        internal const float WIPE_STAGGER = 0.05f;
        internal const float LABEL_TIME = 2.8f;

        internal const int LASER_ROW = 6;
        internal const int LASER_COLUMN = 3;

        /// <summary>Where the single lands: row 6's one gap.</summary>
        internal const int LAND_ROW = LASER_ROW;
        internal const int LAND_COLUMN = 7;

        /// <summary>The beam fires this long after the laser starts to go, and the column starts to clear
        /// this long after that.</summary>
        private const float BEAM_DELAY = 0.2f;
        private const float WIPE_DELAY = 0.1f;

        internal static readonly string[] Rows =
        {
            "........",
            "...g....",
            "..pb....",
            "...u.g..",
            ".g.g....",
            "...p.u..",
            "ggbbuup.",
            "u..g..bb",
        };

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>When the laser starts to go with row 6's clear.</summary>
        internal static float LaserGoneTime() => InfoDemoChoreography.ClearCellShrinkStart(CLEAR_START, LASER_COLUMN);

        internal static float BeamTime() => LaserGoneTime() + BEAM_DELAY;

        internal static float WipeStart() => BeamTime() + WIPE_DELAY;

        /// <summary>The blocks the wipe takes (x = column, y = row): every occupied cell of the laser's
        /// column other than the row the clear already took.</summary>
        internal static Vector2Int[] WipedCells()
        {
            Vector2Int[] column = InfoDemoBoardPattern.OccupiedCells(
                Rows, 0, LASER_COLUMN, InfoDemoLayout.BOARD_SIZE - 1, LASER_COLUMN);
            int count = 0;
            for (int cellIndex = 0; cellIndex < column.Length; cellIndex++)
            {
                if (column[cellIndex].y != LASER_ROW)
                {
                    count++;
                }
            }

            Vector2Int[] wiped = new Vector2Int[count];
            int wipedIndex = 0;
            for (int cellIndex = 0; cellIndex < column.Length; cellIndex++)
            {
                if (column[cellIndex].y != LASER_ROW)
                {
                    wiped[wipedIndex] = column[cellIndex];
                    wipedIndex++;
                }
            }

            return wiped;
        }

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(0), scale);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_3, singleSlot, scale);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(2), scale);

            int laserIcon = InfoDemoSpecialCellChoreography.SpecialCell(
                builder, SpecialCellKind.Laser, LASER_ROW, LASER_COLUMN, out int laserHalo);
            int laserPaint = InfoDemoPaint.SpecialCellGlow(SpecialCellKind.Laser);
            Vector2 laserCell = InfoDemoLayout.Cell(LASER_ROW, LASER_COLUMN);

            // 1. The single completes row 6, which clears — a row clear, taking the laser with it.
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_3, singleSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, LASER_ROW, CLEAR_START);
            InfoDemoSpecialCellChoreography.GoWithCell(builder, laserIcon, laserHalo, LaserGoneTime());

            // 2. Its effect: the line at right angles — column 3 — is wiped end to end, though not full.
            InfoDemoChoreography.Beam(builder, BeamTime(), false, LASER_COLUMN, laserPaint);
            InfoDemoChoreography.ClearCells(builder, WipedCells(), laserCell, WipeStart(), WIPE_STAGGER);

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_PERPENDICULAR_LINE, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 4.6f), 1f,
                InfoDemoPaint.INK, LABEL_TIME, 1.1f);

            return builder.Build();
        }
    }
}
