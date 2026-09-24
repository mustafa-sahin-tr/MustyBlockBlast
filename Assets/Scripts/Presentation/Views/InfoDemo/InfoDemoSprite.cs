namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>Which sprite an <see cref="InfoDemoElementKind.Icon"/> element draws, resolved by the
    /// View through <see cref="IInfoDemoResources"/> so the demo reuses the game's own authored art
    /// rather than re-authoring any. The element's <c>SpriteParameter</c> picks the variant
    /// (e.g. the <c>SpecialCellKind</c> value). An icon is drawn in its resolved tint multiplied by
    /// its paint (white by default), so a plain white shape sprite can take any palette colour.</summary>
    internal enum InfoDemoSprite
    {
        None,

        /// <summary>A special cell's board icon — <c>BoardView.IconSprite</c>/<c>BoardView.IconTint</c>
        /// for the <c>SpecialCellKind</c> in the element's sprite parameter.</summary>
        SpecialCellIcon,

        /// <summary>The shared tick (<c>UiSpriteFactory.CheckMark</c>), white — a progress chip's
        /// "done" badge. The sprite parameter is unused.</summary>
        CheckMark,

        /// <summary>A power-up's own strip/shop icon (<c>PowerUpInventoryView.IconFor</c>) for the
        /// <c>PowerUpKind</c> in the element's sprite parameter (issue #448).</summary>
        PowerUpIcon,

        /// <summary>A hard-edged white disc (<c>UiSpriteFactory.Circle</c>), tinted by the element's
        /// paint — a demo finger's face and border. The sprite parameter is unused.</summary>
        Disc,

        /// <summary>A soft white radial falloff (<c>UiSpriteFactory.RadialGlow</c>), tinted by the
        /// element's paint — a soft drop shadow drawn in the icon layer. The sprite parameter is unused.</summary>
        SoftDisc,

        /// <summary>The coin face the HUD's coin total pill draws (<c>CoinTotalHudView.GetCoinFace</c>),
        /// untinted — a wallet pill, a flying coin, Coin Sower's button (issue #449). The sprite
        /// parameter is unused.</summary>
        Coin,

        /// <summary>The Hold pocket's own empty-slot glyph (<c>HoldSlotView.PocketSprite</c>), issue #449.
        /// The sprite parameter is unused.</summary>
        HoldPocket,
    }
}
