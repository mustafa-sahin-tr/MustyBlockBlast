using System.Globalization;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Localization;
using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// The <see cref="PowerUpKind.CoinSower"/> info demo (issue #449, mockup artboard "Coin Yağmuru"),
    /// authored from the real rule (docs/game-design.md "Coin Sower charges", <c>LevelStartCardView</c>,
    /// <c>CoinSpawnSelector</c>): each banked charge sows one coin cell onto an occupied block — a coin is a
    /// property of a block, never of an empty cell — and a coin cell pays out when a clear destroys it.
    /// <para>
    /// In the real game the charges are spent in bulk at the level-start picker, not from the power-up
    /// strip (Coin Sower has no strip slot or strip icon). The demo compresses that into one scene: a
    /// gold button wearing the HUD's coin face stands in for the picker, and the three coins are sown
    /// onto a board already in play.
    /// </para>
    /// <para>
    /// Choreography (5.6 s loop; board rows top to bottom, '.' empty, letters are block colours):
    /// <code>
    /// row0 ........      the wallet pill sits over the top-right
    /// row1 ........
    /// row2 ...g....
    /// row3 ..bb.u..      coin on (3,3)
    /// row4 .u..gg..
    /// row5 ..pp..b.      coin on (5,2)
    /// row6 g...uu..
    /// row7 bbgg.ppu      coin on (7,5); (7,4) empty
    /// </code>
    /// The strip holds the gold button (count 3) and an L corner, a single and a 1x2; a white wallet
    /// pill reading 120 sits at the board's top-right.
    /// 0.5 s the button is pressed · 0.75 / 1.0 / 1.25 s a coin flies from it to (3,3), (5,2), (7,5),
    /// the count ticking 3 → 2 → 1 → 0, each block turning into a coin cell as its coin lands (1.15 /
    /// 1.4 / 1.65 s) · 2.2 s the single lifts and lands on (7,4) · 3.0 s row 7 clears, taking the coin
    /// cell with it · a coin flies to the wallet, which ticks 120 → 125 at 3.55 s with "+coin" · hold,
    /// fade out from 5.2 s, loop.
    /// </para>
    /// </summary>
    internal static class CoinSowerInfoDemo
    {
        internal const float LOOP_DURATION = 5.6f;

        internal const float PRESS_TIME = 0.5f;
        internal const float FIRST_SOW_TIME = 0.75f;
        internal const float SOW_STAGGER = 0.25f;
        internal const float SOW_FLIGHT_DURATION = 0.4f;
        internal const float PLACE_START = 2.2f;
        internal const float CLEAR_START = 3.0f;
        internal const float PAYOUT_FLIGHT_DURATION = 0.3f;

        internal const int LAND_ROW = 7;
        internal const int LAND_COLUMN = 4;

        /// <summary>The wallet before the payout. Illustrative.</summary>
        internal const int WALLET_START = 120;

        /// <summary>What one coin cell pays when a clear destroys it — the shipped
        /// <c>CurrencyConfig.CoinCellPayout</c> (Assets/Settings/CurrencyConfig.asset). A demo is built
        /// statically, so it mirrors the value rather than reading the config.</summary>
        internal const int COIN_CELL_PAYOUT = 5;

        private const int BUTTON_COUNT = 3;
        private const float COIN_CELL_ICON_SIZE = 0.8f;
        private const float FLYING_COIN_SIZE = 0.7f;
        private const float SOW_ARC_HEIGHT = 1.2f;
        private const float PAYOUT_ARC_HEIGHT = 1.4f;

        /// <summary>The wallet pill at the board's top-right (board units).</summary>
        private static readonly Vector2 WalletCentre = new Vector2(5.9f, 0.05f);
        private static readonly Vector2 WalletSize = new Vector2(2.5f, 0.8f);
        private const float WALLET_COIN_OFFSET_X = -0.78f;
        private const float WALLET_COIN_SIZE = 0.62f;
        private const float WALLET_TEXT_OFFSET_X = 0.3f;
        private const float WALLET_FONT = 0.48f;

        internal static readonly string[] Rows =
        {
            "........",
            "........",
            "...g....",
            "..bb.u..",
            ".u..gg..",
            "..pp..b.",
            "g...uu..",
            "bbgg.ppu",
        };

        /// <summary>The three sown coin cells (x = column, y = row), in sowing order — each on an occupied block.</summary>
        internal static readonly Vector2Int[] CoinCells =
        {
            new Vector2Int(3, 3),
            new Vector2Int(2, 5),
            new Vector2Int(5, 7),
        };

        private static readonly Vector2Int[] CornerShape = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] SingleShape = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] DominoShape = { new Vector2Int(0, 0), new Vector2Int(1, 0) };

        /// <summary>When coin <paramref name="coinIndex"/> leaves the button.</summary>
        internal static float SowTime(int coinIndex) => FIRST_SOW_TIME + (coinIndex * SOW_STAGGER);

        /// <summary>When coin <paramref name="coinIndex"/> lands and its block becomes a coin cell.</summary>
        internal static float ConvertTime(int coinIndex) => SowTime(coinIndex) + SOW_FLIGHT_DURATION;

        /// <summary>When the coin cell on row 7 starts to go with its row's clear.</summary>
        internal static float DestroyedCoinGoneTime()
            => InfoDemoChoreography.ClearCellShrinkStart(CLEAR_START, CoinCells[CoinCells.Length - 1].x);

        /// <summary>When the payout coin reaches the wallet and the balance ticks up.</summary>
        internal static float PayoutTime() => DestroyedCoinGoneTime() + PAYOUT_FLIGHT_DURATION;

        internal static InfoDemoTimeline Build()
        {
            InfoDemoTimelineBuilder builder = new InfoDemoTimelineBuilder(LOOP_DURATION);
            InfoDemoBoardPattern.Apply(builder, Rows);

            InfoDemoPowerUpButton button = InfoDemoPowerUpChoreography.PowerUpButton(
                builder, InfoDemoSprite.Coin, 0, InfoDemoPaint.PLATE_GOLD, BUTTON_COUNT);

            float scale = InfoDemoLayout.TRAY_PIECE_SCALE;
            Vector2 singleSlot = InfoDemoLayout.TraySlot(1, InfoDemoTrayLayout.ChipLeft);
            builder.AddPiece(CornerShape, InfoDemoPaint.BLOCK_2, InfoDemoLayout.TraySlot(0, InfoDemoTrayLayout.ChipLeft), scale);
            int single = builder.AddPiece(SingleShape, InfoDemoPaint.BLOCK_2, singleSlot, scale);
            builder.AddPiece(DominoShape, InfoDemoPaint.BLOCK_5, InfoDemoLayout.TraySlot(2, InfoDemoTrayLayout.ChipLeft), scale);

            Vector2 walletCoin = WalletCentre + new Vector2(WALLET_COIN_OFFSET_X, 0f);
            InfoDemoCounter wallet = AddWallet(builder, walletCoin);

            // 1. Sow: one coin per charge, each landing on a block and making it a coin cell.
            InfoDemoPowerUpChoreography.PressButton(builder, button, PRESS_TIME, false, PRESS_TIME);

            int[] coinIcons = new int[CoinCells.Length];
            for (int coinIndex = 0; coinIndex < CoinCells.Length; coinIndex++)
            {
                Vector2 cell = InfoDemoLayout.Cell(CoinCells[coinIndex].y, CoinCells[coinIndex].x);
                float sowTime = SowTime(coinIndex);
                float convertTime = ConvertTime(coinIndex);

                InfoDemoPowerUpChoreography.SpendCharge(builder, button, sowTime);
                InfoDemoHudChoreography.FlyTo(
                    builder, InfoDemoSprite.Coin, 0, FLYING_COIN_SIZE, button.Centre, cell, sowTime, SOW_FLIGHT_DURATION,
                    SOW_ARC_HEIGHT);

                coinIcons[coinIndex] = builder.AddIcon(
                    InfoDemoSprite.SpecialCellIcon, (int)SpecialCellKind.Coin, cell, COIN_CELL_ICON_SIZE, 0f);
                builder.Fade(coinIcons[coinIndex], convertTime, 0.08f, 0f, 1f);
                builder.Scale(coinIcons[coinIndex], convertTime, 0.3f, 0.3f, 1f, InfoDemoEasing.EaseOutBack);

                int blockId = InfoDemoLayout.BoardBlockId(CoinCells[coinIndex].y, CoinCells[coinIndex].x);
                builder.Flash(blockId, convertTime, 0.3f, 0f, 0.5f, InfoDemoEasing.Pulse);
                InfoDemoChoreography.Burst(builder, cell, InfoDemoPaint.PLATE_GOLD, InfoDemoPaint.NONE, 1f, 2.4f, convertTime, 0.4f);
            }

            // 2. A placement completes row 7, and its coin cell goes with the row...
            InfoDemoChoreography.PlacePiece(
                builder, single, SingleShape, InfoDemoPaint.BLOCK_2, singleSlot, LAND_ROW, LAND_COLUMN, PLACE_START);
            InfoDemoChoreography.ClearRow(builder, LAND_ROW, CLEAR_START);

            float coinGone = DestroyedCoinGoneTime();
            int destroyedIcon = coinIcons[CoinCells.Length - 1];
            builder.Scale(destroyedIcon, coinGone, InfoDemoChoreography.CLEAR_SHRINK_DURATION, 1f, 0.25f, InfoDemoEasing.EaseInCubic);
            builder.Fade(destroyedIcon, coinGone, InfoDemoChoreography.CLEAR_SHRINK_DURATION, 1f, 0f, InfoDemoEasing.EaseInCubic);

            // 3. ...paying out: a coin flies to the wallet, which ticks up.
            Vector2 destroyedCell = InfoDemoLayout.Cell(CoinCells[CoinCells.Length - 1].y, CoinCells[CoinCells.Length - 1].x);
            InfoDemoHudChoreography.FlyTo(
                builder, InfoDemoSprite.Coin, 0, FLYING_COIN_SIZE, destroyedCell, walletCoin, coinGone, PAYOUT_FLIGHT_DURATION,
                PAYOUT_ARC_HEIGHT);

            float payout = PayoutTime();
            InfoDemoHudChoreography.AdvanceCounter(builder, wallet, payout);
            InfoDemoChoreography.FloatLabel(
                builder,
                LocalizationKeys.INFO_POPUP_DEMO_PLUS_COIN,
                WalletCentre + new Vector2(-2.2f, 1.1f),
                0.6f,
                InfoDemoPaint.PLATE_GOLD,
                payout,
                1.1f);

            return builder.Build();
        }

        /// <summary>The wallet pill — a white pill with the HUD's coin face and the balance — present from
        /// loop time 0. Returns its balance counter (<see cref="WALLET_START"/> then plus one payout).</summary>
        private static InfoDemoCounter AddWallet(InfoDemoTimelineBuilder builder, Vector2 coinPosition)
        {
            builder.AddPanel(
                WalletCentre + new Vector2(0f, InfoDemoLayout.FromMockLength(2f)), WalletSize, WalletSize.y * 0.5f,
                InfoDemoPaint.INK, 0.12f);
            builder.AddPanel(WalletCentre, WalletSize, WalletSize.y * 0.5f, InfoDemoPaint.WHITE);
            builder.AddIcon(InfoDemoSprite.Coin, 0, coinPosition, WALLET_COIN_SIZE);

            string[] balances =
            {
                WALLET_START.ToString(CultureInfo.InvariantCulture),
                (WALLET_START + COIN_CELL_PAYOUT).ToString(CultureInfo.InvariantCulture),
            };

            return InfoDemoHudChoreography.Counter(
                builder, balances, WalletCentre + new Vector2(WALLET_TEXT_OFFSET_X, 0f), WalletSize.x * 0.6f, WALLET_FONT,
                InfoDemoPaint.INK);
        }
    }
}
