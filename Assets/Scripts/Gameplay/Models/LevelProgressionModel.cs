using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// Where the player is in the level ladder. One number, because progression is strictly linear:
    /// level N+1 exists only once level N is cleared, so a "which levels are unlocked" set would
    /// always be derivable from this and could only ever disagree with it.
    /// </summary>
    public sealed class LevelProgressionModel
    {
        /// <summary>1-based, matching <c>LevelObjectiveConfig.LevelNumber</c>. A fresh install starts at 1.</summary>
        public ReactiveProperty<int> CurrentLevelNumber { get; } = new ReactiveProperty<int>(1);
    }
}
