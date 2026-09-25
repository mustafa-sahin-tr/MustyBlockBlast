using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// Which <c>ClassicSkinConfig</c> stage the board is drawn in (issue #333): 0 is the plain colour
    /// blocks, and it is the only stage outside a Classic run. The whole board wears one stage at a time,
    /// so this is run-level state, not a per-cell tag. Written only by <c>ClassicSkinSystem</c>.
    /// </summary>
    public sealed class ClassicSkinModel
    {
        public ReactiveProperty<int> StageIndex { get; } = new ReactiveProperty<int>(0);
    }
}
