using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>The mode the current run is being played under. Not persisted: every boot starts endless.</summary>
    public sealed class GameModeModel
    {
        public ReactiveProperty<GameMode> CurrentMode { get; } =
            new ReactiveProperty<GameMode>(GameMode.Endless);
    }
}
