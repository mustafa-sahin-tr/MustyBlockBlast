using System.Globalization;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="SpecialCellKind.Coin"/> info demo (issue #450), authored from the real rule
    /// (<see cref="CoinEffect"/>): a coin cell destroys nothing — destroying it pays coins into the wallet,
    /// double when it sat where a row and a column closed at once (the body says so; the demo shows the
    /// plain single-line case). The payout shown is the default a level-authored coin pays,
    /// <see cref="CoinSowerInfoDemo.COIN_CELL_PAYOUT"/> (<c>CurrencyConfig.CoinCellPayout</c>).
    /// <para>
    /// Choreography (5.0 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0-1 ........
    /// row2 ...u....
    /// row3 ..gg....
    /// row4 .....b..
    /// row5 ..u..bb.
    /// row6 .pbbuugp     the coin rides on (6,4); (6,0) is row 6's one gap
    /// row7 g.....bb
    /// </code>
    /// The strip holds a white wallet chip on the left (the HUD's coin face over the balance, 120) and an
    /// L corner, the single that will be played and a 1x2 to its right.
    /// 0.4 s the single lifts and lands on (6,0) · 1.2 s row 6 clears left to right, the coin cell going
    /// with it · a coin flies in an arc to the wallet, which ticks 120 → 125 at ~1.9 s with "+coin" ·
    /// hold, fade out from 4.6 s, loop.
    /// </para>
    /// </summary>
    internal static class CoinCellInfoDemo
    {
        internal const float LOOP_DURATION = 5.0f;

        internal const float PLACE_START = 0.4f;
        internal const float CLEAR_START = 1.2f;
        internal const float PAYOUT_FLIGHT_DURATION = 0.45f;

        internal const int COIN_ROW = 6;
        internal const int COIN_COLUMN = 4;

        /// <summary>Where the single lands: row 6's one gap.</summary>
        internal const int LAND_ROW = COIN_ROW;
        internal const int LAND_COLUMN = 0;

        private const float FLYING_COIN_SIZE = 0.7f;
        private const float PAYOUT_ARC_HEIGHT = 1.2f;

        /// <summary>Mockup-unit geometry of the wallet chip's contents, relative to the chip centre.</summary>
        private const float MOCK_WALLET_CORNER = 12f;
        private const float MOCK_WALLET_SHADOW_DROP = 2f;
        private const float MOCK_WALLET_COIN_OFFSET_Y = -9f;
        private const float MOCK_WALLET_COIN_SIZE = 18f;
        private const float MOCK_WALLET_COUNTER_OFFSET_Y = 11f;
        private const float MOCK_WALLET_COUNTER_FONT = 14f;
        private const float WALLET_SHADOW_ALPHA = 0.1f;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "...u....",
            "..gg....",
            ".....b..",
            "..u..bb.",
            ".pbbuugp",
            "g.....bb",
        };

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>The wallet chip's coin face, in board units.</summary>
        internal static Vector2 WalletCoinCentre
            => InfoDemoLayout.ChipCentre + new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_WALLET_COIN_OFFSET_Y));

        /// <summary>When the coin cell starts to go with row 6's clear.</summary>
        internal static float CoinGoneTime() => InfoDemoChoreography.ClearCellShrinkStart(CLEAR_START, COIN_COLUMN);

        /// <summary>When the payout coin reaches the wallet and the balance ticks up.</summary>
        internal static float PayoutTime() => CoinGoneTime() + PAYOUT_FLIGHT_DURATION;

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            InfoDemoCounter wallet = AddWalletChip(builder);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), scale);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_3, singleSlot, scale);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_1, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            int coinIcon = InfoDemoSpecialCellChoreography.SpecialCell(
                builder, SpecialCellKind.Coin, COIN_ROW, COIN_COLUMN, out int coinBlock);
            Vector2 coinCell = InfoDemoLayout.Cell(COIN_ROW, COIN_COLUMN);

            // 1. The single completes row 6, which clears, taking the coin cell with it...
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_3, singleSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, COIN_ROW, CLEAR_START);

            float coinGone = CoinGoneTime();
            InfoDemoSpecialCellChoreography.GoWithCell(builder, coinIcon, coinBlock, coinGone);

            // 2. ...paying out: a coin flies to the wallet, which ticks up by one coin cell's payout.
            InfoDemoHudChoreography.FlyTo(
                builder, InfoDemoSprite.Coin, 0, FLYING_COIN_SIZE, coinCell, WalletCoinCentre, coinGone,
                PAYOUT_FLIGHT_DURATION, PAYOUT_ARC_HEIGHT);

            float payout = PayoutTime();
            InfoDemoHudChoreography.AdvanceCounter(builder, wallet, payout);
            InfoDemoChoreography.FloatLabel(
                builder, LocalizationKeys.INFO_POPUP_DEMO_PLUS_COIN, new Vector2((InfoDemoLayout.BOARD_SIZE - 1) * 0.5f, 5.6f),
                1f, InfoDemoPaint.PLATE_GOLD, payout, 1.2f);

            return builder.Build();
        }

        /// <summary>The wallet chip in the strip's left zone — a white rounded chip with the HUD's coin face
        /// over the balance — present from loop time 0. Returns its balance counter
        /// (<see cref="CoinSowerInfoDemo.WALLET_START"/>, then plus one coin cell's payout).</summary>
        private static InfoDemoCounter AddWalletChip(InfoDemoTimelineBuilder builder)
        {
            Vector2 centre = InfoDemoLayout.ChipCentre;
            Vector2 size = new Vector2(
                InfoDemoLayout.FromMockLength(InfoDemoLayout.MOCK_CHIP_WIDTH),
                InfoDemoLayout.FromMockLength(InfoDemoLayout.MOCK_CHIP_HEIGHT));
            float corner = InfoDemoLayout.FromMockLength(MOCK_WALLET_CORNER);

            builder.AddPanel(
                centre + new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_WALLET_SHADOW_DROP)), size, corner,
                InfoDemoPaint.INK, WALLET_SHADOW_ALPHA);
            builder.AddPanel(centre, size, corner, InfoDemoPaint.WHITE);

            // Icon sizes are in board-cell widths (MOCK_CELL), not pitches.
            builder.AddIcon(InfoDemoSprite.Coin, 0, WalletCoinCentre, MOCK_WALLET_COIN_SIZE / InfoDemoLayout.MOCK_CELL);

            string[] balances =
            {
                CoinSowerInfoDemo.WALLET_START.ToString(CultureInfo.InvariantCulture),
                (CoinSowerInfoDemo.WALLET_START + CoinSowerInfoDemo.COIN_CELL_PAYOUT).ToString(CultureInfo.InvariantCulture),
            };

            return InfoDemoHudChoreography.Counter(
                builder, balances, centre + new Vector2(0f, InfoDemoLayout.FromMockLength(MOCK_WALLET_COUNTER_OFFSET_Y)),
                size.x * 0.9f, InfoDemoLayout.FromMockLength(MOCK_WALLET_COUNTER_FONT), InfoDemoPaint.INK);
        }
    }
}
