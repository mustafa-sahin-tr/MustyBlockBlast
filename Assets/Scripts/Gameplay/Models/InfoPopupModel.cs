using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The one info popup on screen, if any. <see cref="Systems.InfoPopupSystem"/> is the only writer;
    /// <see cref="Presentation.Views.InfoPopupView"/> observes <see cref="OpenContent"/> to render (or
    /// hide) the popup.
    /// <para>
    /// The "seen" flags themselves live in <c>PlayerPrefs</c>, read and written by
    /// <see cref="Systems.InfoPopupSystem"/> alone (see <see cref="Systems.InfoPopupSeenKey"/>).
    /// </para>
    /// </summary>
    public sealed class InfoPopupModel
    {
        /// <summary>The popup currently showing, or null when none is. A struct wrapped in
        /// <see cref="System.Nullable{T}"/> rather than a sentinel value, so "no open popup" can never
        /// be confused with a real one.</summary>
        public ReactiveProperty<InfoPopupContent?> OpenContent { get; } = new ReactiveProperty<InfoPopupContent?>(null);
    }
}
