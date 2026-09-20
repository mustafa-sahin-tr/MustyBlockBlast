using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>Runtime audio state for sound effects. Currently just the mute flag.</summary>
    public sealed class SfxModel
    {
        /// <summary>When true, play requests are dropped and nothing is heard.</summary>
        public ReactiveProperty<bool> IsMuted { get; } = new ReactiveProperty<bool>(false);
    }
}
