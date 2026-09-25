using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The Hold pocket info demo (issue #449, mockup artboard "Sakla Cebi") — shown for both ways the Hold
    /// card opens: <see cref="InfoPopupSubjectKind.Hold"/> (first park, tapping the pocket) and
    /// <see cref="InfoPopupSubjectKind.PowerUp"/> with <see cref="PowerUpKind.Hold"/> (the first Hold
    /// charge granted). Authored from the real rule (docs/game-design.md "Hold slot (pocket)",
    /// <c>PowerUpSystem.TryApplyHold</c>, <c>HoldSlotView</c>): a tray piece is <em>dragged</em> onto the
    /// pocket to park it, and every park costs one charge. Parking onto an occupied pocket swaps the two
    /// atomically — the parked piece drops into the tray slot the dragged one just left. Nothing touches
    /// the board. The charge badge is drawn as the real one is: theme block colour 5 while a charge is
    /// held, the offer pink at 0.
    /// <para>
    /// Choreography (5.4 s loop; the board is static):
    /// <code>
    /// row0-3 ........
    /// row4 ....gg..
    /// row5 ..u.gg..
    /// row6 bb.uu..p
    /// row7 ggbbuu.p
    /// </code>
    /// The strip holds, left to right, a J, a 2x2 and a single, and the empty pocket (its own faint glyph)
    /// with a charge badge reading 2 — no power-up button: Hold is never armed from the strip.
    /// 0.55 s a finger picks up the J and from 0.65 s drags it into the pocket · 1.3 s the badge ticks
    /// 2 → 1 · 1.35 s "Held" · 2.15 s the finger picks up the 2x2 and from 2.25 s drags it into the pocket
    /// while the J slides back into the 2x2's slot · 2.9 s the badge ticks 1 → 0 and turns pink ·
    /// 3.0 s "Swapped" · hold, fade out from 5.0 s, loop.
    /// </para>
    /// </summary>
    internal static class HoldInfoDemo
    {
        internal const float LOOP_DURATION = 5.4f;

        internal const float FIRST_GRAB_TIME = 0.55f;
        internal const float FIRST_MOVE_START = 0.65f;
        internal const float SECOND_GRAB_TIME = 2.15f;
        internal const float SECOND_MOVE_START = 2.25f;
        internal const float MOVE_DURATION = 0.6f;
        internal const float FIRST_TICK_TIME = 1.3f;
        internal const float SECOND_TICK_TIME = 2.9f;
        internal const float HELD_LABEL_TIME = 1.35f;
        internal const float SWAPPED_LABEL_TIME = 3.0f;

        internal const int CHARGE_COUNT = 2;

        /// <summary>Mockup-unit strip layout: the three tray pieces' centres and the pocket's centre and side.</summary>
        internal const float MOCK_FIRST_PIECE_X = 50f;
        internal const float MOCK_SECOND_PIECE_X = 112f;
        internal const float MOCK_THIRD_PIECE_X = 154f;
        internal const float MOCK_POCKET_X = 212f;
        internal const float MOCK_POCKET_SIDE = 52f;

        private const float MOCK_POCKET_CORNER = 12f;
        private const float MOCK_POCKET_STROKE = 2f;
        private const float MOCK_POCKET_GLYPH_SIZE = 30f;
        private const float MOCK_BADGE_DIAMETER = 17f;
        private const float MOCK_BADGE_RIM = 2f;
        private const float MOCK_BADGE_FONT = 10f;
        private const float MOCK_BADGE_INSET = 4f;
        private const float POCKET_WELL_ALPHA = 0.06f;
        private const float POCKET_RING_ALPHA = 0.45f;
        private const float POCKET_GLYPH_ALPHA = 0.4f;
        private const float LIFTED_FRACTION = 1.15f;
        private const float LIFT_DURATION = 0.1f;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "........",
            "........",
            "....gg..",
            "..u.gg..",
            "bb.uu..p",
            "ggbbuu.p",
        };

        /// <summary>The J — a vertical bar of three with its foot at the bottom right (catalog <c>j_right</c>).</summary>
        internal static readonly Vector2Int[] FirstShape =
        {
            new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2), new Vector2Int(1, 2),
        };

        internal static readonly Vector2Int[] SecondShape =
        {
            new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1),
        };

        private static readonly Vector2Int[] ThirdShape = { new Vector2Int(0, 0) };

        internal static Vector2 FirstSlot => InfoDemoLayout.StripPoint(MOCK_FIRST_PIECE_X);
        internal static Vector2 SecondSlot => InfoDemoLayout.StripPoint(MOCK_SECOND_PIECE_X);
        internal static Vector2 PocketCentre => InfoDemoLayout.StripPoint(MOCK_POCKET_X);

        /// <summary>
        /// Builds the demo. <paramref name="firstPiece"/> and <paramref name="secondPiece"/> are the J's and
        /// the 2x2's element ids and <paramref name="badge"/> the pocket's charge badge, so tests can follow
        /// them; <see cref="Build()"/> is what the catalog calls.
        /// </summary>
        internal static InfoDemoTimeline Build(out int firstPiece, out int secondPiece, out InfoDemoCountBadge badge)
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            Vector2 pocket = PocketCentre;
            int glyph = AddPocket(builder, pocket);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 firstSlot = FirstSlot;
            Vector2 secondSlot = SecondSlot;
            firstPiece = builder.AddPiece(FirstShape, InfoDemoPaint.BLOCK_3, firstSlot, scale);
            secondPiece = builder.AddPiece(SecondShape, InfoDemoPaint.BLOCK_1, secondSlot, scale);
            builder.AddPiece(ThirdShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.StripPoint(MOCK_THIRD_PIECE_X), scale);

            float badgeOffset = InfoDemoLayout.FromMockLength((MOCK_POCKET_SIDE * 0.5f) - MOCK_BADGE_INSET);
            badge = InfoDemoHudChoreography.CountBadge(
                builder,
                pocket + new Vector2(badgeOffset, -badgeOffset),
                CHARGE_COUNT,
                InfoDemoPaint.HOLD_BADGE,
                InfoDemoLayout.FromMockLength(MOCK_BADGE_DIAMETER),
                InfoDemoLayout.FromMockLength(MOCK_BADGE_RIM),
                InfoDemoLayout.FromMockLength(MOCK_BADGE_FONT),
                true);

            // 1. Drag the J onto the empty pocket: it is parked, and the park costs a charge.
            DragInto(builder, firstPiece, firstSlot, pocket, FIRST_GRAB_TIME, FIRST_MOVE_START);
            builder.Fade(glyph, FIRST_MOVE_START + MOVE_DURATION - 0.15f, 0.15f, POCKET_GLYPH_ALPHA, 0f);
            InfoDemoHudChoreography.TickBadge(builder, badge, FIRST_TICK_TIME, InfoDemoPaint.OFFER_PINK);

            Vector2 labelPosition = new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 5.3f);
            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_HELD, labelPosition, 0.8f, InfoDemoPaint.INK, HELD_LABEL_TIME, 1.1f);

            // 2. Drag the 2x2 onto the occupied pocket: the two swap — the J drops into the slot the 2x2
            // just left — and that park costs a charge too.
            DragInto(builder, secondPiece, secondSlot, pocket, SECOND_GRAB_TIME, SECOND_MOVE_START);
            InfoDemoTrayChoreography.SlidePiece(builder, firstPiece, pocket, secondSlot, scale, scale, SECOND_MOVE_START, MOVE_DURATION);
            InfoDemoHudChoreography.TickBadge(builder, badge, SECOND_TICK_TIME, InfoDemoPaint.OFFER_PINK);

            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_SWAPPED, labelPosition, 0.8f, InfoDemoPaint.INK, SWAPPED_LABEL_TIME, 1.2f);

            return builder.Build();
        }

        internal static InfoDemoTimeline Build() => Build(out int _, out int _, out InfoDemoCountBadge _);

        /// <summary>A finger picks up <paramref name="pieceId"/> at <paramref name="grabTime"/> (it lifts a
        /// little) and drags it from <paramref name="from"/> into the pocket at <paramref name="to"/>.</summary>
        private static void DragInto(
            InfoDemoTimelineBuilder builder, int pieceId, Vector2 from, Vector2 to, float grabTime, float moveStart)
        {
            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            builder.Scale(pieceId, grabTime, LIFT_DURATION, scale, scale * LIFTED_FRACTION, InfoDemoEasing.EaseOutCubic);
            InfoDemoChoreography.Drag(builder, grabTime, from, to, moveStart, MOVE_DURATION);
            InfoDemoTrayChoreography.SlidePiece(builder, pieceId, from, to, scale * LIFTED_FRACTION, scale, moveStart, MOVE_DURATION);
        }

        /// <summary>The pocket's well and outline alone, <paramref name="mockSide"/> mockup units square — for a
        /// pocket that already holds a piece, which hides the glyph (issue #454, Reroll Save's parked piece).</summary>
        internal static void AddPocketWell(InfoDemoTimelineBuilder builder, Vector2 centre, float mockSide)
        {
            float side = InfoDemoLayout.FromMockLength(mockSide);
            Vector2 size = new Vector2(side, side);
            float corner = InfoDemoLayout.FromMockLength(MOCK_POCKET_CORNER * mockSide / MOCK_POCKET_SIDE);

            builder.AddPanel(centre, size, corner, InfoDemoPaint.INK, POCKET_WELL_ALPHA);
            builder.AddRing(
                centre, size, corner, InfoDemoLayout.FromMockLength(MOCK_POCKET_STROKE), InfoDemoPaint.SOFT_INK,
                POCKET_RING_ALPHA);
        }

        /// <summary>The empty pocket as the real HUD draws it — a faint well inside a thin solid outline
        /// (<c>HoldSlotView</c>: the mockup's dashed border, drawn solid) with the pocket's own glyph.
        /// Returns the glyph's element id.</summary>
        private static int AddPocket(InfoDemoTimelineBuilder builder, Vector2 centre)
        {
            AddPocketWell(builder, centre, MOCK_POCKET_SIDE);

            return builder.AddIcon(
                InfoDemoSprite.HoldPocket, 0, centre, MOCK_POCKET_GLYPH_SIZE / InfoDemoLayout.MOCK_CELL, POCKET_GLYPH_ALPHA,
                InfoDemoPaint.SOFT_INK);
        }
    }
}
