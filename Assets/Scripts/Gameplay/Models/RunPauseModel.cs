using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// Whether the current run is "paused" for the purpose of any wall-clock-driven system — today
    /// that's the Timed mode countdown and rolling-window objectives (<c>RollingLineClearWindow</c>,
    /// <c>EarlyScoreRush</c>). Three independent reasons combine with OR: the app is backgrounded, a
    /// modal menu panel is open, or a power-up is armed and being aimed.
    /// <para>
    /// Owned and mutated exclusively by <c>TimerRunSystem</c>, which already gathers these three flags
    /// from its callers (<c>TimerHudView</c>, <c>SettingsPanelView</c>, <c>LevelPathPanelView</c>,
    /// <c>PowerUpSystem</c>) for the countdown. Every other consumer — currently
    /// <c>ObjectiveSystem</c> — only ever reads <see cref="IsPaused"/>.
    /// </para>
    /// </summary>
    public sealed class RunPauseModel
    {
        public ReactiveProperty<bool> IsPaused { get; } = new ReactiveProperty<bool>(false);
    }
}
