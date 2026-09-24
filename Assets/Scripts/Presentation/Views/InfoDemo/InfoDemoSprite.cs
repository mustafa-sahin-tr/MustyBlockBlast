namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>Which sprite an <see cref="InfoDemoElementKind.Icon"/> element draws, resolved by the
    /// View through <see cref="IInfoDemoResources"/> so the demo reuses the game's own authored art
    /// rather than re-authoring any. The element's <c>SpriteParameter</c> picks the variant
    /// (e.g. the <c>SpecialCellKind</c> value).</summary>
    internal enum InfoDemoSprite
    {
        None,

        /// <summary>A special cell's board icon — <c>BoardView.IconSprite</c>/<c>BoardView.IconTint</c>
        /// for the <c>SpecialCellKind</c> in the element's sprite parameter.</summary>
        SpecialCellIcon,

        /// <summary>The shared tick (<c>UiSpriteFactory.CheckMark</c>), white — a progress chip's
        /// "done" badge. The sprite parameter is unused.</summary>
        CheckMark,
    }
}
