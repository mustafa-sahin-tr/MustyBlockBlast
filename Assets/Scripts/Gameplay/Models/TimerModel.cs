using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The live countdown of a timed run. Mutated exclusively by <c>TimerRunSystem</c>; the HUD only
    /// subscribes. In endless runs it stays at zero and not running, which is what hides the HUD.
    /// </summary>
    public sealed class TimerModel
    {
        /// <summary>Seconds left in the match. Clamped to zero; never negative.</summary>
        public ReactiveProperty<float> RemainingSeconds { get; } = new ReactiveProperty<float>(0f);

        /// <summary>True while the countdown is counting down. False in endless runs and after it expires.</summary>
        public ReactiveProperty<bool> IsRunning { get; } = new ReactiveProperty<bool>(false);

        /// <summary>True once remaining time drops below the low-time warning threshold. Drives HUD warning visuals.</summary>
        public ReactiveProperty<bool> IsLowTime { get; } = new ReactiveProperty<bool>(false);
    }
}
